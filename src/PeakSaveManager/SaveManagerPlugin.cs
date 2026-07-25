using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Photon.Pun;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using Zorro.Core;
using Zorro.Core.Serizalization;
using Zorro.UI;
using GamePlayer = Player;
using RealtimePlayer = Photon.Realtime.Player;
using UnityObject = UnityEngine.Object;

namespace PEAKSaveManager;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class SaveManagerPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.lucasandersen.peakallyouneedsavemanager";
    public const string PluginName = "The All You Need Save Manager";
    public const string PluginVersion = "1.0.0";

    private const string SaveFilePattern = "*.json";

    private const int UiWindowId = 129193;

    private const string PauseMenuButtonObjectName = "PeakSaveManagerButton";

    private const float PendingSeedLifetimeSeconds = 90f;

    private static readonly MethodInfo RecalculateLookDirectionsMethod = AccessTools.Method(typeof(Character), "RecalculateLookDirections");

    private static readonly MethodInfo CampfireUpdateLitMethod = AccessTools.Method(typeof(Campfire), "UpdateLit");

    private static readonly MethodInfo CampfireHideLogsMethod = AccessTools.Method(typeof(Campfire), "HideLogs");

    private static readonly FieldInfo LuggageStateField = AccessTools.Field(typeof(Luggage), "state");

    private static readonly MethodInfo LuggageOpenRpcMethod = AccessTools.Method(typeof(Luggage), "OpenLuggageRPC");

    private static readonly MethodInfo MenuWindowOpenMethod = AccessTools.Method(typeof(MenuWindow), "Open");

    private static readonly MethodInfo MenuWindowCloseMethod = AccessTools.Method(typeof(MenuWindow), "Close");

    private static readonly MethodInfo RespawnChestSetSpentMethod = AccessTools.PropertySetter(typeof(RespawnChest), nameof(RespawnChest.IsSpent));

    private static readonly MethodInfo RespawnChestSetRevivedPlayersMethod = AccessTools.PropertySetter(typeof(RespawnChest), "HasRevivedPlayers");

    private static readonly MethodInfo RespawnChestGetRevivedPlayersMethod = AccessTools.PropertyGetter(typeof(RespawnChest), "HasRevivedPlayers");

    private static readonly FieldInfo RespawnChestRevivedPlayersField = AccessTools.Field(typeof(RespawnChest), "hasRevivedPlayers");

    private static readonly MethodInfo RunManagerRpcSyncTimeMethod = AccessTools.Method(typeof(RunManager), "RPC_SyncTime", new[] { typeof(float), typeof(bool) });

    private static readonly FieldInfo RunManagerTimerActiveField = AccessTools.Field(typeof(RunManager), "timerActive");

    private static readonly PropertyInfo RopeShooterAmmoProperty = AccessTools.Property(typeof(RopeShooter), "Ammo");

    private static readonly MethodInfo RopeShooterSyncRpcMethod = AccessTools.Method(typeof(RopeShooter), "Sync_Rpc", new[] { typeof(bool) });

    private static readonly FieldInfo ItemTotalUsesField = AccessTools.Field(typeof(Item), "totalUses");

    private static readonly FieldInfo ItemDataField = AccessTools.Field(typeof(Item), "data");

    private static readonly FieldInfo MagicBeanVineCurrentLengthField = AccessTools.Field(typeof(MagicBeanVine), "currentLength");

    private static readonly FieldInfo MagicBeanVineInitialLengthField = AccessTools.Field(typeof(MagicBeanVine), "initialLength");

    private static readonly FieldInfo MagicBeanVineMaxLengthField = AccessTools.Field(typeof(MagicBeanVine), "maxLength");

    private static readonly FieldInfo CloudFungusAlreadyBrokeField = AccessTools.Field(typeof(CloudFungus), "alreadyBroke");

    private static readonly FieldInfo CloudFungusTimeAliveField = AccessTools.Field(typeof(CloudFungus), "timeAlive");

    private static readonly FieldInfo OptionableIntHasDataField = AccessTools.Field(typeof(OptionableIntItemData), "HasData");

    private static readonly FieldInfo OptionableIntValueField = AccessTools.Field(typeof(OptionableIntItemData), "Value");

    private static readonly FieldInfo OptionableBoolHasDataField = AccessTools.Field(typeof(OptionableBoolItemData), "HasData");

    private static readonly FieldInfo OptionableBoolValueField = AccessTools.Field(typeof(OptionableBoolItemData), "Value");

    private static readonly FieldInfo CharacterDataCheckpointFlagsField = AccessTools.Field(typeof(CharacterData), "checkpointFlags");

    private static readonly FieldInfo CheckpointFlagStatusesField = AccessTools.Field(typeof(CheckpointFlag), "currentStatuses");

    private static readonly FieldInfo CheckpointFlagPlanterField = AccessTools.Field(typeof(CheckpointFlag), "planterCharacter");

    private static readonly FieldInfo CharacterItemsCurrentSelectedSlotField = AccessTools.Field(typeof(CharacterItems), "currentSelectedSlot");

    private static readonly MethodInfo CharacterItemsEquipSlotMethod = AccessTools.Method(typeof(CharacterItems), "EquipSlot", new[] { typeof(Optionable<byte>) });

    private static readonly ConstructorInfo OptionableByteConstructor = AccessTools.Constructor(typeof(Optionable<byte>), new[] { typeof(byte), typeof(byte) });

    private static readonly FieldInfo OptionableByteHasValueField = AccessTools.Field(typeof(Optionable<byte>), "hasValue");

    private static readonly FieldInfo OptionableByteValueField = AccessTools.Field(typeof(Optionable<byte>), "value");

    private static readonly MethodInfo MirageLuggageSetStateMethod = AccessTools.Method(typeof(MirageLuggage), "setMirageState", new[] { typeof(float) });

    private static readonly FieldInfo MirageLuggageRenderersField = AccessTools.Field(typeof(MirageLuggage), "renderers");

    private static readonly MethodInfo DayNightTimeStringGetterMethod = AccessTools.PropertyGetter(typeof(DayNightManager), "timeString");

    private static readonly MethodInfo DayNightTimeStringSetterMethod = AccessTools.PropertySetter(typeof(DayNightManager), "timeString");

    private static readonly FieldInfo DayNightTimeStringField = AccessTools.Field(typeof(DayNightManager), "timeString");

    private static readonly HashSet<CharacterAfflictions.STATUSTYPE> SkippedSavedStatuses = new HashSet<CharacterAfflictions.STATUSTYPE>
    {
        CharacterAfflictions.STATUSTYPE.Weight
    };

    private static SaveManagerPlugin Instance;

    private static int? PendingSeedForLoad;

    private static float PendingSeedSetRealtime;

    private Harmony harmony;

    private ConfigEntry<bool> autoSaveEnabled;

    private ConfigEntry<float> autoSaveIntervalSeconds;

    private string saveDirectory;

    private string preferredSaveDirectory;

    private string fallbackSaveDirectory;

    private bool showUi;

    private bool isLoading;

    private float lastAutoSaveTime;

    private bool lastSceneLoadSucceeded = true;

    private Rect windowRect = new Rect(34f, 34f, 820f, 620f);

    private Vector2 fileScroll = Vector2.zero;

    private string newSaveName = "";

    private readonly List<SaveListEntry> saveEntries = new List<SaveListEntry>();

    private string statusMessage = "";

    private Color statusColor = Color.white;

    private float statusMessageUntil;

    private bool hasConfirmationPending;

    private string confirmationTitle = "";

    private string confirmationMessage = "";

    private Action confirmationAction;

    private bool stylesBuilt;

    private GUIStyle windowStyle;

    private GUIStyle sectionStyle;

    private GUIStyle titleStyle;

    private GUIStyle subtitleStyle;

    private GUIStyle normalLabelStyle;

    private GUIStyle errorLabelStyle;

    private GUIStyle softButtonStyle;

    private GUIStyle dangerButtonStyle;

    private GUIStyle textFieldStyle;

    private GUIStyle cardStyle;

    private GUIStyle cardWarningStyle;

    private Texture2D overlayTexture;

    private Texture2D windowTexture;

    private Texture2D sectionTexture;

    private Texture2D cardTexture;

    private Texture2D warningCardTexture;

    private Texture2D buttonTexture;

    private Texture2D buttonHoverTexture;

    private Texture2D dangerButtonTexture;

    private Texture2D textFieldTexture;

    private SaveManagerPausePage pauseMenuPage;

    private UIPageHandler pauseMenuPageHandler;

    private UIPage pauseMenuMainPage;

    private Button pauseMenuButtonTemplate;

    private bool quitPendingSaveDecision;

    private PauseMenuMainPage quitPendingPage;

    private bool allowVanillaQuitClick;

    private void Awake()
    {
        Instance = this;

        autoSaveEnabled = Config.Bind("AutoSave", "Enable AutoSave", true, "Automatically create rolling autosaves");
        autoSaveIntervalSeconds = Config.Bind("AutoSave", "Interval Seconds", 300f, "Seconds between autosaves");

        preferredSaveDirectory = Path.Combine(Paths.GameRootPath, "PeakSaves");
        fallbackSaveDirectory = Path.Combine(Application.persistentDataPath, "PeakSaves");
        saveDirectory = preferredSaveDirectory;
        EnsureSaveDirectoryReady(showStatus: false);

        harmony = new Harmony(PluginGuid);
        harmony.PatchAll();

        RefreshSaveFileList();
        SetStatus("Save manager ready.", Color.cyan, 2f);
        Logger.LogInfo("Loaded The All You Need Save Manager 1.0.0 by Lucas Andersen");
    }

    private void OnDestroy()
    {
        try
        {
            harmony?.UnpatchSelf();
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Failed to unpatch Harmony cleanly: {ex}");
        }

        DestroyUiResources();
    }

    private void Update()
    {
        if (showUi)
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        if (!showUi && !isLoading && autoSaveEnabled.Value)
        {
            if (Time.time - lastAutoSaveTime > Mathf.Max(15f, autoSaveIntervalSeconds.Value))
            {
                if (CanAutoSaveNow())
                {
                    TrySaveGame("Autosave");
                }

                lastAutoSaveTime = Time.time;
            }
        }
    }

    private bool CanAutoSaveNow()
    {
        if (!CanSaveNow(showReason: false))
        {
            return false;
        }

        string sceneName = SceneManager.GetActiveScene().name;
        if (string.IsNullOrWhiteSpace(sceneName) || !sceneName.StartsWith("Level_", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (LoadingScreenHandler.loading)
        {
            return false;
        }

        return true;
    }

    private void OnGUI()
    {
        if (!showUi)
        {
            return;
        }

        EnsureStyles();
        DrawOverlay();

        GUI.depth = -1000;
        windowRect = GUI.Window(UiWindowId, windowRect, DrawMainWindow, "PEAK ALL YOU NEED SAVE MANAGER", windowStyle);
    }

    private void DrawMainWindow(int _)
    {
        float statusRemaining = statusMessageUntil - Time.unscaledTime;
        bool inAirport = IsInAirportScene();

        GUILayout.BeginVertical();

        if (!string.IsNullOrEmpty(statusMessage) && statusRemaining > 0f)
        {
            Color previousColor = GUI.color;
            GUI.color = statusColor;
            GUILayout.Label(statusMessage, sectionStyle, GUILayout.Height(30f));
            GUI.color = previousColor;
        }

        if (isLoading)
        {
            GUILayout.Label("Loading save and synchronizing players...", sectionStyle, GUILayout.Height(28f));
        }

        if (hasConfirmationPending)
        {
            DrawConfirmationPrompt();
            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0f, 0f, windowRect.width, 24f));
            return;
        }

        GUILayout.BeginVertical(sectionStyle);
        GUILayout.Label("Create Save", subtitleStyle);
        GUILayout.Space(4f);

        if (inAirport)
        {
            GUILayout.Label("You cannot save in the Airport.", errorLabelStyle);
            GUILayout.Space(4f);
        }

        GUILayout.BeginHorizontal();
        GUILayout.Label("Name", normalLabelStyle, GUILayout.Width(55f));
        newSaveName = GUILayout.TextField(newSaveName, textFieldStyle, GUILayout.ExpandWidth(true));
        GUILayout.EndHorizontal();

        GUILayout.Space(6f);
        GUILayout.BeginHorizontal();
        GUI.enabled = !isLoading && !inAirport;
        if (GUILayout.Button("Quick Save", softButtonStyle, GUILayout.Height(28f), GUILayout.Width(120f)))
        {
            TrySaveGame("Quick Save");
        }

        GUI.enabled = !isLoading && !inAirport && !string.IsNullOrWhiteSpace(newSaveName);
        if (GUILayout.Button("Save", softButtonStyle, GUILayout.Height(28f), GUILayout.Width(80f)))
        {
            string saveName = newSaveName.Trim();
            newSaveName = string.Empty;
            TrySaveGame(saveName);
        }
        GUI.enabled = true;
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();

        GUILayout.Space(8f);

        GUILayout.BeginVertical(sectionStyle);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Saved Runs", subtitleStyle);
        GUILayout.FlexibleSpace();
        GUI.enabled = !isLoading;
        if (GUILayout.Button("Refresh", softButtonStyle, GUILayout.Width(90f), GUILayout.Height(24f)))
        {
            RefreshSaveFileList();
        }
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        GUILayout.Space(6f);

        fileScroll = GUILayout.BeginScrollView(fileScroll, GUILayout.Height(390f));
        if (saveEntries.Count == 0)
        {
            GUILayout.Label("No save files found.", normalLabelStyle);
        }
        else
        {
            foreach (SaveListEntry entry in saveEntries)
            {
                DrawSaveCard(entry);
                GUILayout.Space(5f);
            }
        }
        GUILayout.EndScrollView();
        GUILayout.EndVertical();

        GUILayout.Space(8f);
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Close", softButtonStyle, GUILayout.Width(120f), GUILayout.Height(30f)))
        {
            showUi = false;
        }
        GUILayout.EndHorizontal();

        GUILayout.EndVertical();
        GUI.DragWindow(new Rect(0f, 0f, windowRect.width, 24f));
    }

    private void DrawSaveCard(SaveListEntry entry)
    {
        GUIStyle style = entry.isCompatible ? cardStyle : cardWarningStyle;
        bool canSaveNow = CanSaveNow(showReason: false);
        GUILayout.BeginVertical(style);

        GUILayout.BeginHorizontal();
        GUILayout.Label(entry.fileName, titleStyle);
        GUILayout.FlexibleSpace();
        GUILayout.Label(FormatBytes(entry.fileSize), normalLabelStyle, GUILayout.Width(78f));
        GUILayout.EndHorizontal();

        if (entry.metadata != null)
        {
            string timeText = entry.metadata.savedAtUtc == default
                ? entry.fileTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                : entry.metadata.savedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

            int playerCount = entry.metadata.playerCount;
            if (playerCount <= 0)
            {
                playerCount = 0;
            }

            string levelText = Safe(entry.metadata.levelName);
            if (entry.metadata.levelNumber.HasValue)
            {
                levelText += $" (#{entry.metadata.levelNumber.Value})";
            }

            GUILayout.Label($"Saved: {timeText}", normalLabelStyle);
            GUILayout.Label(
                $"Level: {levelText}   Segment: {Safe(ToDisplaySegmentName(entry.metadata.currentSegmentName, entry.metadata.biomeId))}",
                normalLabelStyle
            );
            GUILayout.Label($"Seed: {entry.metadata.levelSeed}   Ascent: {entry.metadata.ascent}   Players: {playerCount}", normalLabelStyle);
            GUILayout.Label(FormatRunSummary(entry.metadata), normalLabelStyle);
        }
        else
        {
            GUILayout.Label($"File Time: {entry.fileTime:yyyy-MM-dd HH:mm:ss}", normalLabelStyle);
        }

        if (!entry.isCompatible)
        {
            GUILayout.Label("Incompatible: " + entry.incompatibilityReason, errorLabelStyle);
        }

        GUILayout.Space(4f);
        GUILayout.BeginHorizontal();
        GUI.enabled = !isLoading && entry.isCompatible;
        if (GUILayout.Button("Load", softButtonStyle, GUILayout.Height(24f), GUILayout.Width(90f)))
        {
            StartLoadFromUi(entry.fullPath);
        }

        GUI.enabled = !isLoading && canSaveNow;
        if (GUILayout.Button("Overwrite", softButtonStyle, GUILayout.Height(24f), GUILayout.Width(100f)))
        {
            string saveNameHint = entry.metadata != null && !string.IsNullOrWhiteSpace(entry.metadata.saveName)
                ? entry.metadata.saveName
                : Path.GetFileNameWithoutExtension(entry.fileName);
            string targetPath = entry.fullPath;
            string targetName = entry.fileName;
            RequestConfirmation(
                "Overwrite Save?",
                $"Overwrite '{targetName}' with your current run state?",
                () =>
                {
                    TryOverwriteSave(targetPath, saveNameHint);
                }
            );
        }

        GUI.enabled = !isLoading;
        if (GUILayout.Button("Delete", dangerButtonStyle, GUILayout.Height(24f), GUILayout.Width(90f)))
        {
            string targetPath = entry.fullPath;
            string targetName = entry.fileName;
            RequestConfirmation(
                "Delete Save?",
                $"Delete '{targetName}' permanently? This cannot be undone.",
                () => TryDeleteSave(targetPath)
            );
        }
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        GUILayout.EndVertical();
    }

    private void StartLoadFromUi(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return;
        }

        showUi = false;
        ClearPendingConfirmation();
        pauseMenuPage?.PrepareForLoad();
        ClosePauseMenuForLoad();
        StartCoroutine(LoadSaveRoutine(fullPath));
    }

    private void ClosePauseMenuForLoad()
    {
        try
        {
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }

            PauseMainMenu pauseMainMenu = UnityObject.FindFirstObjectByType<PauseMainMenu>();
            if (pauseMainMenu != null)
            {
                CloseMenuWindow(pauseMainMenu);
                pauseMainMenu.gameObject.SetActive(false);
            }

            if (pauseMenuPage != null)
            {
                pauseMenuPage.gameObject.SetActive(false);
            }

            if (pauseMenuPageHandler != null)
            {
                pauseMenuPageHandler.gameObject.SetActive(false);
            }

            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Failed to close pause menu before load: {ex.Message}");
        }
    }

    private void ToggleUi()
    {
        if (PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient)
        {
            SetStatus("Only the host can open Save Manager.", Color.yellow, 2.5f);
            return;
        }

        showUi = !showUi;
        if (showUi)
        {
            RefreshSaveFileList();
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }
        else
        {
            ClearPendingConfirmation();
        }
    }

    private void RequestConfirmation(string title, string message, Action confirmedAction)
    {
        if (confirmedAction == null)
        {
            return;
        }

        hasConfirmationPending = true;
        confirmationTitle = string.IsNullOrWhiteSpace(title) ? "Are you sure?" : title;
        confirmationMessage = string.IsNullOrWhiteSpace(message) ? "Please confirm this action." : message;
        confirmationAction = confirmedAction;
    }

    private void ClearPendingConfirmation()
    {
        hasConfirmationPending = false;
        confirmationTitle = string.Empty;
        confirmationMessage = string.Empty;
        confirmationAction = null;
    }

    private void ExecutePendingConfirmation()
    {
        Action pendingAction = confirmationAction;
        ClearPendingConfirmation();

        if (pendingAction == null)
        {
            return;
        }

        try
        {
            pendingAction.Invoke();
        }
        catch (Exception ex)
        {
            Logger.LogError($"Confirmation action failed: {ex}");
            SetStatus("Action failed. Check BepInEx log.", Color.red, 4f);
        }
    }

    private void DrawConfirmationPrompt()
    {
        GUILayout.Space(8f);
        GUILayout.BeginVertical(sectionStyle);
        GUILayout.Label(confirmationTitle, subtitleStyle);
        GUILayout.Space(4f);
        GUILayout.Label(confirmationMessage, normalLabelStyle);

        GUILayout.Space(10f);
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        GUI.enabled = !isLoading;
        if (GUILayout.Button("Cancel", softButtonStyle, GUILayout.Height(30f), GUILayout.Width(120f)))
        {
            ClearPendingConfirmation();
        }

        GUI.enabled = !isLoading;
        if (GUILayout.Button("Confirm", dangerButtonStyle, GUILayout.Height(30f), GUILayout.Width(120f)))
        {
            ExecutePendingConfirmation();
        }
        GUI.enabled = true;
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();
    }

    internal static void TryAttachPauseMenuButton(PauseMenuMainPage pauseMenuPage)
    {
        Instance?.AttachPauseMenuButton(pauseMenuPage);
    }

    private void AttachPauseMenuButton(PauseMenuMainPage pauseMenuPage)
    {
        if (pauseMenuPage == null)
        {
            return;
        }

        try
        {
            FieldInfo settingsButtonField = AccessTools.Field(typeof(PauseMenuMainPage), "m_settingsButton");
            Button settingsButton = settingsButtonField?.GetValue(pauseMenuPage) as Button;
            if (settingsButton == null || settingsButton.transform == null || settingsButton.transform.parent == null)
            {
                return;
            }

            Transform parent = settingsButton.transform.parent;
            Transform existing = parent.Find(PauseMenuButtonObjectName);
            Button saveManagerButton;

            if (existing == null)
            {
                GameObject buttonObject = Instantiate(settingsButton.gameObject, parent, worldPositionStays: false);
                buttonObject.name = PauseMenuButtonObjectName;

                RectTransform sourceRect = settingsButton.transform as RectTransform;
                RectTransform buttonRect = buttonObject.transform as RectTransform;
                if (sourceRect != null && buttonRect != null)
                {
                    buttonRect.anchorMin = sourceRect.anchorMin;
                    buttonRect.anchorMax = sourceRect.anchorMax;
                    buttonRect.pivot = sourceRect.pivot;
                    buttonRect.sizeDelta = sourceRect.sizeDelta;
                    buttonRect.localScale = sourceRect.localScale;
                    buttonRect.anchoredPosition = sourceRect.anchoredPosition + new Vector2(0f, -Mathf.Max(50f, sourceRect.rect.height + 8f));
                }

                buttonObject.transform.SetSiblingIndex(settingsButton.transform.GetSiblingIndex() + 1);

                MonoBehaviour[] behaviors = buttonObject.GetComponentsInChildren<MonoBehaviour>(true);
                foreach (MonoBehaviour behavior in behaviors)
                {
                    if (behavior != null && behavior.GetType().Name == "LocalizedText")
                    {
                        Destroy(behavior);
                    }
                }

                SetPauseMenuButtonText(buttonObject, "SAVE MANAGER");
                saveManagerButton = buttonObject.GetComponent<Button>();
            }
            else
            {
                saveManagerButton = existing.GetComponent<Button>();
            }

            if (saveManagerButton == null)
            {
                return;
            }

            saveManagerButton.onClick.RemoveAllListeners();
            saveManagerButton.onClick.AddListener(() => OpenPauseMenuSavePage(pauseMenuPage));
            saveManagerButton.interactable = !PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient;
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Failed to attach pause menu save button: {ex.Message}");
        }
    }

    private void OpenPauseMenuSavePage(PauseMenuMainPage pauseMainPage)
    {
        if (pauseMainPage == null)
        {
            return;
        }

        if (PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient)
        {
            SetStatus("Only the host can open Save Manager.", Color.yellow, 2.5f);
            return;
        }

        UIPageHandler pageHandler = pauseMainPage.GetPageHandler<UIPageHandler>();
        if (pageHandler == null)
        {
            Logger.LogWarning("Pause menu page handler not found.");
            return;
        }

        FieldInfo settingsButtonField = AccessTools.Field(typeof(PauseMenuMainPage), "m_settingsButton");
        Button settingsButton = settingsButtonField?.GetValue(pauseMainPage) as Button;
        SaveManagerPausePage savePage = EnsurePauseMenuSavePage(pageHandler, pauseMainPage, settingsButton);
        if (savePage == null)
        {
            Logger.LogWarning("Save manager pause page could not be created.");
            return;
        }

        savePage.RefreshFromSource();
        pageHandler.TransistionToPage(savePage, new SetActivePageTransistion());
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    private SaveManagerPausePage EnsurePauseMenuSavePage(UIPageHandler pageHandler, UIPage mainPage, Button settingsButtonTemplate)
    {
        if (pageHandler == null || mainPage == null)
        {
            return null;
        }

        if (pauseMenuPage != null && pauseMenuPageHandler == pageHandler)
        {
            pauseMenuPage.Initialize(this, pageHandler, mainPage, settingsButtonTemplate);
            return pauseMenuPage;
        }

        SaveManagerPausePage existing = pageHandler.GetPage<SaveManagerPausePage>() as SaveManagerPausePage;
        if (existing != null)
        {
            pauseMenuPage = existing;
            pauseMenuPageHandler = pageHandler;
            pauseMenuMainPage = mainPage;
            pauseMenuButtonTemplate = settingsButtonTemplate;
            pauseMenuPage.Initialize(this, pageHandler, mainPage, settingsButtonTemplate);
            return pauseMenuPage;
        }

        GameObject pageObject = new GameObject(
            "PauseMenuSaveManagerPage",
            typeof(RectTransform),
            typeof(Image),
            typeof(SaveManagerPausePage)
        );

        RectTransform rect = pageObject.GetComponent<RectTransform>();
        rect.SetParent(pageHandler.transform, worldPositionStays: false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image background = pageObject.GetComponent<Image>();
        background.color = new Color(0.02f, 0.05f, 0.10f, 0.75f);

        SaveManagerPausePage createdPage = pageObject.GetComponent<SaveManagerPausePage>();
        FieldInfo pageHandlerField = AccessTools.Field(typeof(UIPage), "pageHandler");
        pageHandlerField?.SetValue(createdPage, pageHandler);

        createdPage.Initialize(this, pageHandler, mainPage, settingsButtonTemplate);
        pageObject.SetActive(false);

        FieldInfo pagesField = AccessTools.Field(typeof(UIPageHandler), "_pages");
        object pagesObject = pagesField?.GetValue(pageHandler);
        if (pagesObject != null)
        {
            MethodInfo removeMethod = pagesObject.GetType().GetMethod("Remove", new[] { typeof(Type) });
            removeMethod?.Invoke(pagesObject, new object[] { typeof(SaveManagerPausePage) });

            MethodInfo addMethod = pagesObject.GetType().GetMethod("Add", new[] { typeof(Type), typeof(UIPage) });
            if (addMethod != null)
            {
                addMethod.Invoke(pagesObject, new object[] { typeof(SaveManagerPausePage), createdPage });
            }
            else
            {
                MethodInfo fallbackAddMethod = pagesObject.GetType().GetMethod("Add");
                fallbackAddMethod?.Invoke(pagesObject, new object[] { typeof(SaveManagerPausePage), createdPage });
            }
        }

        pauseMenuPage = createdPage;
        pauseMenuPageHandler = pageHandler;
        pauseMenuMainPage = mainPage;
        pauseMenuButtonTemplate = settingsButtonTemplate;
        return createdPage;
    }

    internal static bool TryInterceptPauseQuit(PauseMenuMainPage page)
    {
        return Instance != null && Instance.InterceptPauseQuit(page);
    }

    private bool InterceptPauseQuit(PauseMenuMainPage page)
    {
        if (allowVanillaQuitClick || page == null)
        {
            return false;
        }

        if (!CanSaveNow(showReason: false))
        {
            return false;
        }

        FieldInfo confirmWindowField = AccessTools.Field(typeof(PauseMenuMainPage), "confirmWindow");
        FieldInfo confirmOkField = AccessTools.Field(typeof(PauseMenuMainPage), "m_confirmOkButton");
        FieldInfo confirmCancelField = AccessTools.Field(typeof(PauseMenuMainPage), "m_confirmCancelButton");
        FieldInfo confirmTextField = AccessTools.Field(typeof(PauseMenuMainPage), "confirmText");

        MenuWindow confirmWindow = confirmWindowField?.GetValue(page) as MenuWindow;
        Button okButton = confirmOkField?.GetValue(page) as Button;
        Button cancelButton = confirmCancelField?.GetValue(page) as Button;
        LocalizedText confirmText = confirmTextField?.GetValue(page) as LocalizedText;

        if (confirmWindow == null || okButton == null || cancelButton == null)
        {
            return false;
        }

        quitPendingSaveDecision = true;
        quitPendingPage = page;

        OpenMenuWindow(confirmWindow);
        confirmText?.SetText("Would you like to save your game before quitting?");
        SetPauseMenuButtonText(okButton.gameObject, "YES");
        SetPauseMenuButtonText(cancelButton.gameObject, "NO");

        okButton.onClick.RemoveAllListeners();
        okButton.onClick.AddListener(() =>
        {
            CloseMenuWindow(confirmWindow);
            OpenPauseMenuSavePage(page);
        });

        cancelButton.onClick.RemoveAllListeners();
        cancelButton.onClick.AddListener(() =>
        {
            CloseMenuWindow(confirmWindow);
            quitPendingSaveDecision = false;
            quitPendingPage = null;
            ContinueWithVanillaQuit(page);
        });

        return true;
    }

    private void ContinueWithVanillaQuit(PauseMenuMainPage page)
    {
        if (page == null)
        {
            return;
        }

        try
        {
            allowVanillaQuitClick = true;
            MethodInfo quitMethod = AccessTools.Method(typeof(PauseMenuMainPage), "Quit");
            if (quitMethod != null)
            {
                quitMethod.Invoke(page, null);
                return;
            }

            MethodInfo onQuitClickedMethod = AccessTools.Method(typeof(PauseMenuMainPage), "OnQuitClicked");
            onQuitClickedMethod?.Invoke(page, null);
        }
        finally
        {
            allowVanillaQuitClick = false;
        }
    }

    private static void SetPauseMenuButtonText(GameObject buttonObject, string text)
    {
        if (buttonObject == null)
        {
            return;
        }

        TMP_Text tmpText = buttonObject.GetComponentInChildren<TMP_Text>(true);
        if (tmpText != null)
        {
            tmpText.text = text;
            tmpText.enableAutoSizing = true;
            tmpText.fontSizeMin = 13f;
            tmpText.fontSizeMax = 21f;
            tmpText.fontStyle = FontStyles.UpperCase;
            tmpText.overflowMode = TextOverflowModes.Ellipsis;
            return;
        }

        Text uiText = buttonObject.GetComponentInChildren<Text>(true);
        if (uiText != null)
        {
            uiText.text = text;
            uiText.fontStyle = FontStyle.Normal;
            uiText.resizeTextForBestFit = true;
            uiText.resizeTextMinSize = 13;
            uiText.resizeTextMaxSize = 21;
        }
    }

    private static void OpenMenuWindow(MenuWindow window)
    {
        if (window == null)
        {
            return;
        }

        try
        {
            MenuWindowOpenMethod?.Invoke(window, null);
        }
        catch
        {
            window.gameObject.SetActive(true);
        }
    }

    private static void CloseMenuWindow(MenuWindow window)
    {
        if (window == null)
        {
            return;
        }

        try
        {
            MenuWindowCloseMethod?.Invoke(window, null);
        }
        catch
        {
            window.gameObject.SetActive(false);
        }
    }

    private static int GetLuggageState(Luggage luggage)
    {
        if (luggage == null || LuggageStateField == null)
        {
            return 0;
        }

        try
        {
            object raw = LuggageStateField.GetValue(luggage);
            return raw != null ? Convert.ToInt32(raw, CultureInfo.InvariantCulture) : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static void SetLuggageStateRaw(Luggage luggage, int rawState)
    {
        if (luggage == null || LuggageStateField == null)
        {
            return;
        }

        int clamped = Mathf.Max(0, rawState);
        try
        {
            Type fieldType = LuggageStateField.FieldType;
            if (fieldType != null && fieldType.IsEnum)
            {
                Array enumValues = Enum.GetValues(fieldType);
                if (enumValues != null && enumValues.Length > 0)
                {
                    int max = enumValues.Cast<object>()
                        .Select(value => Convert.ToInt32(value, CultureInfo.InvariantCulture))
                        .DefaultIfEmpty(0)
                        .Max();
                    clamped = Mathf.Clamp(clamped, 0, max);
                }
            }

            object boxed = fieldType != null && fieldType.IsEnum
                ? Enum.ToObject(fieldType, clamped)
                : (object)clamped;
            LuggageStateField.SetValue(luggage, boxed);
        }
        catch
        {
            // Ignore raw luggage-state restoration failures.
        }
    }

    private static PhotonView GetPhotonView(Component component)
    {
        if (component == null)
        {
            return null;
        }

        PhotonView view = component.GetComponent<PhotonView>();
        if (view != null)
        {
            return view;
        }

        return component.GetComponentInParent<PhotonView>();
    }

    private static void SetRespawnChestState(RespawnChest respawnChest, bool isSpent, bool hasRevivedPlayers)
    {
        if (respawnChest == null)
        {
            return;
        }

        try
        {
            RespawnChestSetSpentMethod?.Invoke(respawnChest, new object[] { isSpent });
            SetRespawnChestHasRevivedPlayers(respawnChest, hasRevivedPlayers);
        }
        catch
        {
            // Ignore state restore failures on game updates.
        }
    }

    private static bool GetRespawnChestHasRevivedPlayers(RespawnChest respawnChest)
    {
        if (respawnChest == null)
        {
            return false;
        }

        try
        {
            object value = RespawnChestGetRevivedPlayersMethod?.Invoke(respawnChest, null);
            if (value is bool boolValue)
            {
                return boolValue;
            }

            value = RespawnChestRevivedPlayersField?.GetValue(respawnChest);
            if (value is bool fieldValue)
            {
                return fieldValue;
            }
        }
        catch
        {
            // Ignore version-specific reflection failures.
        }

        return false;
    }

    private static void SetRespawnChestHasRevivedPlayers(RespawnChest respawnChest, bool hasRevivedPlayers)
    {
        if (respawnChest == null)
        {
            return;
        }

        try
        {
            if (RespawnChestSetRevivedPlayersMethod != null)
            {
                RespawnChestSetRevivedPlayersMethod.Invoke(respawnChest, new object[] { hasRevivedPlayers });
                return;
            }

            RespawnChestRevivedPlayersField?.SetValue(respawnChest, hasRevivedPlayers);
        }
        catch
        {
            // Ignore version-specific reflection failures.
        }
    }

    private static string GetDayNightTimeString(DayNightManager dayNight)
    {
        if (dayNight == null)
        {
            return string.Empty;
        }

        try
        {
            object value = DayNightTimeStringGetterMethod?.Invoke(dayNight, null) ?? DayNightTimeStringField?.GetValue(dayNight);
            if (value is string stringValue)
            {
                return stringValue;
            }
        }
        catch
        {
            // Ignore version-specific reflection failures.
        }

        return string.Empty;
    }

    private static void SetDayNightTimeString(DayNightManager dayNight, string timeString)
    {
        if (dayNight == null || string.IsNullOrWhiteSpace(timeString))
        {
            return;
        }

        try
        {
            if (DayNightTimeStringSetterMethod != null)
            {
                DayNightTimeStringSetterMethod.Invoke(dayNight, new object[] { timeString });
                return;
            }

            DayNightTimeStringField?.SetValue(dayNight, timeString);
        }
        catch
        {
            // Ignore version-specific reflection failures.
        }
    }

    private bool CanSaveNow(bool showReason)
    {
        if (IsInAirportScene())
        {
            if (showReason)
            {
                SetStatus("You cannot save in the Airport.", Color.yellow, 3f);
            }

            return false;
        }

        if (PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient)
        {
            if (showReason)
            {
                SetStatus("Only host can save shared runs.", Color.yellow, 3f);
            }

            return false;
        }

        return true;
    }

    private bool TrySaveGame(string saveName)
    {
        if (!CanSaveNow(showReason: true))
        {
            return false;
        }

        try
        {
            if (!EnsureSaveDirectoryReady(showStatus: true))
            {
                return false;
            }

            string displaySaveName = NormalizeSaveDisplayName(saveName);
            SaveEnvelope snapshot = CaptureSnapshot(displaySaveName);
            if (snapshot.players == null || snapshot.players.Count == 0)
            {
                SetStatus("Could not find an active player to save. Try again in-run.", Color.yellow, 4f);
                Logger.LogWarning("Save aborted: snapshot captured 0 players.");
                return false;
            }

            string fullPath = ResolveSavePathForName(displaySaveName);

            string json = JsonConvert.SerializeObject(snapshot, Formatting.Indented);
            File.WriteAllText(fullPath, json);
            if (displaySaveName.Equals("Autosave", StringComparison.OrdinalIgnoreCase))
            {
                CleanupLegacyAutosaves(fullPath);
            }

            RefreshSaveFileList();
            SetStatus($"Saved: {displaySaveName}", Color.green, 3f);
            Logger.LogInfo($"Saved run to {fullPath}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Save failed: {ex}");
            SetStatus(BuildSaveFailureStatus(ex), Color.red, 6f);
            return false;
        }
    }

    private bool TryOverwriteSave(string fullPath, string saveNameHint)
    {
        if (!CanSaveNow(showReason: true))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(fullPath))
        {
            SetStatus("Invalid save path.", Color.red, 4f);
            return false;
        }

        try
        {
            if (!EnsureSaveDirectoryReady(showStatus: true))
            {
                return false;
            }

            string saveName = NormalizeSaveDisplayName(
                string.IsNullOrWhiteSpace(saveNameHint)
                    ? Path.GetFileNameWithoutExtension(fullPath)
                    : saveNameHint.Trim()
            );

            SaveEnvelope snapshot = CaptureSnapshot(saveName);
            if (snapshot.players == null || snapshot.players.Count == 0)
            {
                SetStatus("Could not find an active player to overwrite from. Try again in-run.", Color.yellow, 4f);
                Logger.LogWarning($"Overwrite aborted for '{fullPath}': snapshot captured 0 players.");
                return false;
            }

            string json = JsonConvert.SerializeObject(snapshot, Formatting.Indented);
            File.WriteAllText(fullPath, json);

            RefreshSaveFileList();
            SetStatus($"Overwrote save: {saveName}", Color.green, 3f);
            Logger.LogInfo($"Overwrote run at {fullPath}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Overwrite failed for '{fullPath}': {ex}");
            SetStatus(BuildSaveFailureStatus(ex), Color.red, 6f);
            return false;
        }
    }

    internal void UiRefreshSaveList()
    {
        RefreshSaveFileList();
    }

    internal List<SaveListEntry> UiGetSaveEntries()
    {
        return new List<SaveListEntry>(saveEntries);
    }

    internal void UiQuickSave()
    {
        bool saved = TrySaveGame("Quick Save");
        MaybeCompletePendingQuitAfterSave(saved);
    }

    internal void UiNamedSave(string saveName)
    {
        if (string.IsNullOrWhiteSpace(saveName))
        {
            SetStatus("Enter a save name first.", Color.yellow, 3f);
            return;
        }

        bool saved = TrySaveGame(saveName.Trim());
        MaybeCompletePendingQuitAfterSave(saved);
    }

    internal void UiLoadSave(string fullPath)
    {
        StartLoadFromUi(fullPath);
    }

    internal void UiOverwriteSave(string fullPath, string saveNameHint)
    {
        bool saved = TryOverwriteSave(fullPath, saveNameHint);
        MaybeCompletePendingQuitAfterSave(saved);
    }

    internal void UiDeleteSave(string fullPath)
    {
        TryDeleteSave(fullPath);
    }

    internal bool UiCanSaveNow(bool showReason = false)
    {
        return CanSaveNow(showReason);
    }

    internal bool UiIsLoading()
    {
        return isLoading;
    }

    internal string UiCurrentStatus()
    {
        if (string.IsNullOrWhiteSpace(statusMessage))
        {
            return string.Empty;
        }

        if (statusMessageUntil <= Time.unscaledTime)
        {
            return string.Empty;
        }

        return statusMessage;
    }

    internal bool UiHasPendingQuitSaveDecision()
    {
        return quitPendingSaveDecision;
    }

    internal void UiQuickSaveAndQuit()
    {
        if (!CanSaveNow(showReason: true))
        {
            return;
        }

        bool saved = TrySaveGame("Quick Save");
        if (saved)
        {
            CompletePendingQuit();
        }
    }

    internal void UiNamedSaveAndQuit(string saveName)
    {
        if (!CanSaveNow(showReason: true))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(saveName))
        {
            SetStatus("Enter a save name before Save & Quit.", Color.yellow, 3f);
            return;
        }

        bool saved = TrySaveGame(saveName.Trim());
        if (saved)
        {
            CompletePendingQuit();
        }
    }

    internal void UiQuitWithoutSaving()
    {
        CompletePendingQuit();
    }

    internal void UiCancelPendingQuit()
    {
        quitPendingSaveDecision = false;
        quitPendingPage = null;
    }

    private void MaybeCompletePendingQuitAfterSave(bool saveSucceeded)
    {
        if (saveSucceeded && quitPendingSaveDecision)
        {
            CompletePendingQuit();
        }
    }

    private void CompletePendingQuit()
    {
        PauseMenuMainPage page = quitPendingPage;
        quitPendingSaveDecision = false;
        quitPendingPage = null;
        ContinueWithVanillaQuit(page);
    }

    private SaveEnvelope CaptureSnapshot(string saveName)
    {
        string sceneName = SceneManager.GetActiveScene().name;

        LevelGeneration levelGeneration = UnityObject.FindFirstObjectByType<LevelGeneration>();
        int levelSeed = levelGeneration != null ? levelGeneration.seed : 0;

        int currentSegment = 0;
        string segmentName = Segment.Beach.ToString();
        if (MapHandler.Exists)
        {
            MapHandler mapHandler = UnityObject.FindFirstObjectByType<MapHandler>();
            if (mapHandler != null)
            {
                Segment segment = mapHandler.GetCurrentSegment();
                currentSegment = (int)segment;
                segmentName = segment.ToString();
            }
        }

        int? dailyLevelIndex = null;
        string biomeId = string.Empty;
        try
        {
            NextLevelService nextLevelService = GameHandler.GetService<NextLevelService>();
            if (nextLevelService != null && nextLevelService.HasReceivedLevelIndex)
            {
                dailyLevelIndex = nextLevelService.NextLevelIndexOrFallback;
                biomeId = SingletonAsset<MapBaker>.Instance.GetBiomeID(dailyLevelIndex.Value + NextLevelService.debugLevelIndexOffset);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Could not capture NextLevelService metadata: {ex.Message}");
        }

        string levelName = ResolveLevelName(sceneName, dailyLevelIndex);
        int? levelNumber = ParseLevelNumber(levelName);
        CaptureRunMetadata(
            out int? runDay,
            out float? runTimeSeconds,
            out bool? runTimerActive,
            out float? timeOfDay,
            out string inGameTime
        );

        SaveEnvelope envelope = new SaveEnvelope
        {
            pluginGuid = PluginGuid,
            pluginVersion = PluginVersion,
            metadata = new SaveMetadata
            {
                saveName = saveName,
                savedAtUtc = DateTime.UtcNow,
                sceneName = sceneName,
                levelName = levelName,
                levelNumber = levelNumber,
                dailyLevelIndex = dailyLevelIndex,
                biomeId = biomeId,
                levelSeed = levelSeed,
                currentSegment = currentSegment,
                currentSegmentName = segmentName,
                ascent = Ascents.currentAscent,
                runDay = runDay,
                runTimeSeconds = runTimeSeconds,
                runTimerActive = runTimerActive,
                timeOfDay = timeOfDay,
                inGameTime = inGameTime
            }
        };

        CapturePlayers(envelope.players);
        envelope.metadata.playerCount = envelope.players.Count;
        CaptureCampfires(envelope.campfires);
        CaptureLuggageStates(envelope.luggageStates);
        CaptureContainerStates(envelope.containerStates);
        CaptureGroundItems(envelope.groundItems);
        CaptureWorldObjects(envelope.worldObjects);

        return envelope;
    }

    private static void CaptureRunMetadata(
        out int? runDay,
        out float? runTimeSeconds,
        out bool? runTimerActive,
        out float? timeOfDay,
        out string inGameTime
    )
    {
        runDay = null;
        runTimeSeconds = null;
        runTimerActive = null;
        timeOfDay = null;
        inGameTime = string.Empty;

        try
        {
            DayNightManager dayNight = DayNightManager.instance ?? UnityObject.FindFirstObjectByType<DayNightManager>();
            if (dayNight != null)
            {
                runDay = Mathf.Max(0, dayNight.dayCount);
                timeOfDay = dayNight.timeOfDay;

                string currentTimeString = GetDayNightTimeString(dayNight);
                if (!string.IsNullOrWhiteSpace(currentTimeString))
                {
                    inGameTime = currentTimeString;
                }
                else
                {
                    inGameTime = dayNight.FloatToTimeString(dayNight.timeOfDay);
                }
            }
        }
        catch (Exception ex)
        {
            Instance?.Logger.LogWarning($"Could not capture day/night metadata: {ex.Message}");
        }

        try
        {
            RunManager runManager = RunManager.Instance ?? UnityObject.FindFirstObjectByType<RunManager>();
            if (runManager != null)
            {
                runTimeSeconds = Mathf.Max(0f, runManager.timeSinceRunStarted);
                runTimerActive = TryGetRunTimerActive(runManager);
            }
        }
        catch (Exception ex)
        {
            Instance?.Logger.LogWarning($"Could not capture run timer metadata: {ex.Message}");
        }
    }

    private static bool? TryGetRunTimerActive(RunManager runManager)
    {
        if (runManager == null || RunManagerTimerActiveField == null)
        {
            return null;
        }

        try
        {
            object raw = RunManagerTimerActiveField.GetValue(runManager);
            if (raw is bool value)
            {
                return value;
            }
        }
        catch
        {
            // Ignore reflection failures on game updates.
        }

        return null;
    }

    private void CapturePlayers(List<PlayerSnapshot> output)
    {
        GamePlayer[] players = UnityObject.FindObjectsByType<GamePlayer>(FindObjectsSortMode.None);
        foreach (GamePlayer player in players)
        {
            try
            {
                output.Add(CapturePlayer(player));
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to snapshot player: {ex.Message}");
            }
        }

        if (output.Count > 0)
        {
            return;
        }

        Character[] characters = UnityObject.FindObjectsByType<Character>(FindObjectsSortMode.None);
        foreach (Character character in characters)
        {
            if (character == null)
            {
                continue;
            }

            GamePlayer fallbackPlayer = ((Component)character).GetComponent<GamePlayer>()
                ?? ((Component)character).GetComponentInParent<GamePlayer>();
            if (fallbackPlayer == null)
            {
                continue;
            }

            try
            {
                output.Add(CapturePlayer(fallbackPlayer));
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to snapshot fallback player: {ex.Message}");
            }

            if (output.Count > 0)
            {
                break;
            }
        }

        if (output.Count > 0)
        {
            return;
        }

        foreach (GamePlayer fallbackPlayer in EnumeratePlayersFromHandler())
        {
            if (fallbackPlayer == null)
            {
                continue;
            }

            try
            {
                output.Add(CapturePlayer(fallbackPlayer));
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to snapshot handler fallback player: {ex.Message}");
            }

            if (output.Count > 0)
            {
                break;
            }
        }
    }

    private static IEnumerable<GamePlayer> EnumeratePlayersFromHandler()
    {
        Type playerHandlerType = AccessTools.TypeByName("PlayerHandler");
        if (playerHandlerType == null)
        {
            yield break;
        }

        MethodInfo getAllPlayersMethod = AccessTools.Method(playerHandlerType, "GetAllPlayers");
        if (getAllPlayersMethod == null)
        {
            yield break;
        }

        object result;
        try
        {
            result = getAllPlayersMethod.Invoke(null, null);
        }
        catch
        {
            yield break;
        }

        if (result is IEnumerable rawPlayers)
        {
            foreach (object rawPlayer in rawPlayers)
            {
                if (rawPlayer is GamePlayer gamePlayer)
                {
                    yield return gamePlayer;
                }
            }
        }
    }

    private PlayerSnapshot CapturePlayer(GamePlayer player)
    {
        string playerName = "Unknown";
        int actorNumber = 0;
        PhotonView photonView = GetPhotonView((Component)player);
        if (photonView == null && player != null && player.character != null)
        {
            photonView = GetPhotonView((Component)player.character);
        }

        if (photonView != null)
        {
            RealtimePlayer owner = photonView.Owner;
            if (owner != null)
            {
                playerName = owner.NickName;
                actorNumber = owner.ActorNumber;
            }
            else
            {
                actorNumber = photonView.OwnerActorNr;
            }
        }

        Character character = player.character;
        Vector3 position = character != null ? GetCharacterPosition(character) : ((Component)player).transform.position;
        Vector3 rotation = character != null ? ((Component)character).transform.eulerAngles : ((Component)player).transform.eulerAngles;
        CaptureCharacterVelocity(character, out Vector3 velocity, out Vector3 angularVelocity);
        InventorySnapshot inventory;
        try
        {
            inventory = CaptureInventory(player, character);
        }
        catch (Exception ex)
        {
            Instance?.Logger.LogWarning($"Inventory capture fallback for player '{playerName}': {ex.Message}");
            inventory = new InventorySnapshot();
        }

        PlayerSnapshot snapshot = new PlayerSnapshot
        {
            playerName = playerName,
            actorNumber = actorNumber,
            position = new Vector3Snapshot(position),
            rotation = new Vector3Snapshot(rotation),
            velocity = new Vector3Snapshot(velocity),
            angularVelocity = new Vector3Snapshot(angularVelocity),
            character = CaptureCharacter(character),
            inventory = inventory
        };

        return snapshot;
    }

    private static CharacterSnapshot CaptureCharacter(Character character)
    {
        CharacterSnapshot snapshot = new CharacterSnapshot();
        if (character == null)
        {
            return snapshot;
        }

        snapshot.dead = character.data.dead;
        snapshot.passedOut = character.data.passedOut;
        snapshot.fullyPassedOut = character.data.fullyPassedOut;
        snapshot.isGrounded = character.data.isGrounded;
        snapshot.isClimbing = character.data.isClimbing;
        snapshot.isRopeClimbing = character.data.isRopeClimbing;
        snapshot.isVineClimbing = character.data.isVineClimbing;
        snapshot.isSprinting = character.data.isSprinting;
        snapshot.currentStamina = character.data.currentStamina;
        snapshot.extraStamina = character.data.extraStamina;
        snapshot.sinceGrounded = character.data.sinceGrounded;
        snapshot.lookValues = new Vector2Snapshot(character.data.lookValues);
        snapshot.checkpointFlagPaths = CaptureCharacterCheckpointPaths(character);
        snapshot.statuses = CaptureCharacterStatuses(character);
        return snapshot;
    }

    private static List<string> CaptureCharacterCheckpointPaths(Character character)
    {
        List<string> output = new List<string>();
        if (character == null || character.data == null || CharacterDataCheckpointFlagsField == null)
        {
            return output;
        }

        try
        {
            object raw = CharacterDataCheckpointFlagsField.GetValue(character.data);
            if (raw is IEnumerable enumerable)
            {
                foreach (object entry in enumerable)
                {
                    if (entry is CheckpointFlag flag && flag != null)
                    {
                        string path = BuildTransformPath(((Component)flag).transform);
                        if (!string.IsNullOrWhiteSpace(path))
                        {
                            output.Add(path);
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore checkpoint list capture failures on game updates.
        }

        return output;
    }

    private static void CaptureCharacterVelocity(Character character, out Vector3 velocity, out Vector3 angularVelocity)
    {
        velocity = Vector3.zero;
        angularVelocity = Vector3.zero;
        if (character == null)
        {
            return;
        }

        Rigidbody hipRig = character.refs != null && character.refs.hip != null ? character.refs.hip.Rig : null;
        if (hipRig != null)
        {
            velocity = hipRig.linearVelocity;
            angularVelocity = hipRig.angularVelocity;
            return;
        }

        if (character.data != null)
        {
            velocity = character.data.avarageVelocity;
            angularVelocity = character.data.avarageLastFrameVelocity;
        }
    }

    private static List<CharacterStatusSnapshot> CaptureCharacterStatuses(Character character)
    {
        List<CharacterStatusSnapshot> statuses = new List<CharacterStatusSnapshot>();
        if (character == null || character.refs == null || character.refs.afflictions == null)
        {
            return statuses;
        }

        CharacterAfflictions afflictions = character.refs.afflictions;
        Array allStatusTypes = Enum.GetValues(typeof(CharacterAfflictions.STATUSTYPE));
        for (int i = 0; i < allStatusTypes.Length; i++)
        {
            CharacterAfflictions.STATUSTYPE statusType = (CharacterAfflictions.STATUSTYPE)allStatusTypes.GetValue(i);
            if (SkippedSavedStatuses.Contains(statusType))
            {
                continue;
            }

            float amount = afflictions.GetCurrentStatus(statusType);
            if (amount <= 0f)
            {
                continue;
            }

            statuses.Add(new CharacterStatusSnapshot
            {
                statusType = statusType.ToString(),
                amount = amount
            });
        }

        return statuses;
    }

    private static InventorySnapshot CaptureInventory(GamePlayer player, Character character)
    {
        InventorySnapshot snapshot = new InventorySnapshot();
        if (player == null)
        {
            return snapshot;
        }

        if (player.itemSlots != null)
        {
            for (int i = 0; i < player.itemSlots.Length; i++)
            {
                snapshot.mainSlots.Add(CaptureItemSlot(player.itemSlots[i]));
            }
        }

        snapshot.tempSlot = CaptureItemSlot(player.tempFullSlot);
        snapshot.hasBackpack = player.backpackSlot != null && player.backpackSlot.hasBackpack;

        if (snapshot.hasBackpack && TryGetBackpackData(player, out BackpackData backpackData) && backpackData != null)
        {
            if (backpackData.itemSlots != null)
            {
                for (int i = 0; i < backpackData.itemSlots.Length; i++)
                {
                    snapshot.backpackSlots.Add(CaptureItemSlot(backpackData.itemSlots[i]));
                }
            }
        }

        CaptureEquippedSelection(player, character, snapshot);

        return snapshot;
    }

    private static void CaptureEquippedSelection(GamePlayer player, Character character, InventorySnapshot snapshot)
    {
        if (snapshot == null)
        {
            return;
        }

        Item heldItem = character != null && character.data != null ? character.data.currentItem : null;
        if (heldItem != null)
        {
            snapshot.heldItemId = heldItem.itemID;
        }

        if (TryGetCharacterSelectedSlot(character, out int selectedSlot))
        {
            snapshot.selectedSlotId = selectedSlot;
            if (player != null && player.itemSlots != null && selectedSlot >= 0 && selectedSlot < player.itemSlots.Length)
            {
                snapshot.equippedMainSlotIndex = selectedSlot;
            }
        }

        if (!snapshot.equippedMainSlotIndex.HasValue
            && snapshot.heldItemId != ushort.MaxValue
            && player != null
            && player.itemSlots != null)
        {
            for (int i = 0; i < player.itemSlots.Length; i++)
            {
                ItemSlot slot = player.itemSlots[i];
                if (slot != null && slot.prefab != null && slot.prefab.itemID == snapshot.heldItemId)
                {
                    snapshot.equippedMainSlotIndex = i;
                    break;
                }
            }
        }

        if (snapshot.heldItemId != ushort.MaxValue
            && player != null
            && player.tempFullSlot != null
            && player.tempFullSlot.prefab != null
            && player.tempFullSlot.prefab.itemID == snapshot.heldItemId)
        {
            snapshot.equippedTempSlot = true;
        }

        if (snapshot.heldItemId != ushort.MaxValue
            && player != null
            && TryGetBackpackData(player, out BackpackData backpackData)
            && backpackData != null
            && backpackData.itemSlots != null)
        {
            for (int i = 0; i < backpackData.itemSlots.Length; i++)
            {
                ItemSlot slot = backpackData.itemSlots[i];
                if (slot != null && slot.prefab != null && slot.prefab.itemID == snapshot.heldItemId)
                {
                    snapshot.equippedBackpackSlotIndex = i;
                    break;
                }
            }
        }
    }

    private static ItemSlotSnapshot CaptureItemSlot(ItemSlot slot)
    {
        if (slot == null || slot.prefab == null)
        {
            return ItemSlotSnapshot.Empty();
        }

        ItemSlotSnapshot snapshot = new ItemSlotSnapshot
        {
            itemId = slot.prefab.itemID
        };
        CaptureItemUsageData(slot.data, snapshot);
        return snapshot;
    }

    private static void CaptureItemUsageData(ItemInstanceData data, ItemSlotSnapshot snapshot)
    {
        if (data == null || snapshot == null)
        {
            return;
        }

        if (TryReadItemUsageInt(data, DataEntryKey.ItemUses, out int itemUses))
        {
            snapshot.itemUses = itemUses;
        }

        if (TryReadItemUsageInt(data, DataEntryKey.PetterItemUses, out int petterUses))
        {
            snapshot.petterItemUses = petterUses;
        }

        if (TryReadItemUsageFloat(data, DataEntryKey.UseRemainingPercentage, out float useRemaining))
        {
            snapshot.useRemainingPercentage = useRemaining;
        }

        if (TryReadItemUsageBool(data, DataEntryKey.Used, out bool used))
        {
            snapshot.used = used;
        }

        if (TryReadItemUsageFloat(data, DataEntryKey.Fuel, out float fuel))
        {
            snapshot.fuel = fuel;
        }

        if (TryReadItemUsageInt(data, DataEntryKey.CookedAmount, out int cookedAmount))
        {
            snapshot.cookedAmount = cookedAmount;
        }

        if (TryReadItemUsageBool(data, DataEntryKey.FlareActive, out bool flareActive))
        {
            snapshot.flareActive = flareActive;
        }

        if (TryReadItemUsageFloat(data, DataEntryKey.ScreamTime, out float screamTime))
        {
            snapshot.screamTime = screamTime;
        }

        if (TryReadItemUsageBool(data, DataEntryKey.SpawnedBees, out bool spawnedBees))
        {
            snapshot.spawnedBees = spawnedBees;
        }

        CaptureGenericItemData(data, snapshot.dataEntries);
    }

    private static void CaptureItemUsageData(ItemInstanceData data, GroundItemSnapshot snapshot)
    {
        if (data == null || snapshot == null)
        {
            return;
        }

        if (TryReadItemUsageInt(data, DataEntryKey.ItemUses, out int itemUses))
        {
            snapshot.itemUses = itemUses;
        }

        if (TryReadItemUsageInt(data, DataEntryKey.PetterItemUses, out int petterUses))
        {
            snapshot.petterItemUses = petterUses;
        }

        if (TryReadItemUsageFloat(data, DataEntryKey.UseRemainingPercentage, out float useRemaining))
        {
            snapshot.useRemainingPercentage = useRemaining;
        }

        if (TryReadItemUsageBool(data, DataEntryKey.Used, out bool used))
        {
            snapshot.used = used;
        }

        if (TryReadItemUsageFloat(data, DataEntryKey.Fuel, out float fuel))
        {
            snapshot.fuel = fuel;
        }

        if (TryReadItemUsageInt(data, DataEntryKey.CookedAmount, out int cookedAmount))
        {
            snapshot.cookedAmount = cookedAmount;
        }

        if (TryReadItemUsageBool(data, DataEntryKey.FlareActive, out bool flareActive))
        {
            snapshot.flareActive = flareActive;
        }

        if (TryReadItemUsageFloat(data, DataEntryKey.ScreamTime, out float screamTime))
        {
            snapshot.screamTime = screamTime;
        }

        if (TryReadItemUsageBool(data, DataEntryKey.SpawnedBees, out bool spawnedBees))
        {
            snapshot.spawnedBees = spawnedBees;
        }

        CaptureGenericItemData(data, snapshot.dataEntries);
    }

    private static bool TryReadItemUsageInt(ItemInstanceData data, DataEntryKey key, out int value)
    {
        value = 0;
        if (!TryGetRawDataEntry(data, key, out DataEntryValue rawValue) || rawValue == null)
        {
            return false;
        }

        if (rawValue is OptionableIntItemData optionableIntData
            && TryReadOptionableInt(optionableIntData, out int optionableInt))
        {
            value = optionableInt;
            return true;
        }

        if (rawValue is IntItemData intData)
        {
            value = intData.Value;
            return true;
        }

        if (rawValue is FloatItemData floatData)
        {
            value = Mathf.RoundToInt(floatData.Value);
            return true;
        }

        if (rawValue is BoolItemData boolData)
        {
            value = boolData.Value ? 1 : 0;
            return true;
        }

        return false;
    }

    private static bool TryReadItemUsageFloat(ItemInstanceData data, DataEntryKey key, out float value)
    {
        value = 0f;
        if (!TryGetRawDataEntry(data, key, out DataEntryValue rawValue) || rawValue == null)
        {
            return false;
        }

        if (rawValue is OptionableIntItemData optionableIntData
            && TryReadOptionableInt(optionableIntData, out int optionableInt))
        {
            value = optionableInt;
            return true;
        }

        if (rawValue is FloatItemData floatData)
        {
            value = floatData.Value;
            return true;
        }

        if (rawValue is IntItemData intData)
        {
            value = intData.Value;
            return true;
        }

        return false;
    }

    private static bool TryReadItemUsageBool(ItemInstanceData data, DataEntryKey key, out bool value)
    {
        value = false;
        if (!TryGetRawDataEntry(data, key, out DataEntryValue rawValue) || rawValue == null)
        {
            return false;
        }

        if (rawValue is OptionableBoolItemData optionableBoolData
            && TryReadOptionableBool(optionableBoolData, out bool optionableBool))
        {
            value = optionableBool;
            return true;
        }

        if (rawValue is BoolItemData boolData)
        {
            value = boolData.Value;
            return true;
        }

        if (rawValue is IntItemData intData)
        {
            value = intData.Value > 0;
            return true;
        }

        return false;
    }

    private static bool TryReadOptionableInt(OptionableIntItemData data, out int value)
    {
        value = 0;
        if (data == null)
        {
            return false;
        }

        bool hasData = true;
        if (OptionableIntHasDataField != null)
        {
            try
            {
                object rawHasData = OptionableIntHasDataField.GetValue(data);
                if (rawHasData is bool hasDataValue)
                {
                    hasData = hasDataValue;
                }
            }
            catch
            {
                // Ignore and assume data is present.
            }
        }

        if (!hasData)
        {
            return false;
        }

        if (OptionableIntValueField != null)
        {
            try
            {
                object rawValue = OptionableIntValueField.GetValue(data);
                if (rawValue is int intValue)
                {
                    value = intValue;
                    return true;
                }
            }
            catch
            {
                // Ignore reflection failures.
            }
        }

        return false;
    }

    private static bool TryReadOptionableBool(OptionableBoolItemData data, out bool value)
    {
        value = false;
        if (data == null)
        {
            return false;
        }

        bool hasData = true;
        if (OptionableBoolHasDataField != null)
        {
            try
            {
                object rawHasData = OptionableBoolHasDataField.GetValue(data);
                if (rawHasData is bool hasDataValue)
                {
                    hasData = hasDataValue;
                }
            }
            catch
            {
                // Ignore and assume data is present.
            }
        }

        if (!hasData)
        {
            return false;
        }

        if (OptionableBoolValueField != null)
        {
            try
            {
                object rawValue = OptionableBoolValueField.GetValue(data);
                if (rawValue is bool boolValue)
                {
                    value = boolValue;
                    return true;
                }
            }
            catch
            {
                // Ignore reflection failures.
            }
        }

        return false;
    }

    private static bool TryGetRawDataEntry(ItemInstanceData data, DataEntryKey key, out DataEntryValue value)
    {
        value = null;
        if (data == null || data.data == null)
        {
            return false;
        }

        try
        {
            if (data.data.TryGetValue(key, out DataEntryValue rawValue) && rawValue != null)
            {
                value = rawValue;
                return true;
            }
        }
        catch
        {
            // Ignore raw dictionary lookup failures.
        }

        return false;
    }

    private static void CaptureGenericItemData(ItemInstanceData data, List<ItemDataEntrySnapshot> output)
    {
        if (data == null || output == null || data.data == null)
        {
            return;
        }

        output.Clear();
        foreach (KeyValuePair<DataEntryKey, DataEntryValue> pair in data.data)
        {
            if (TryCreateItemDataEntrySnapshot(pair.Key, pair.Value, out ItemDataEntrySnapshot entrySnapshot))
            {
                output.Add(entrySnapshot);
            }
        }
    }

    private static bool TryCreateItemDataEntrySnapshot(DataEntryKey key, DataEntryValue value, out ItemDataEntrySnapshot snapshot)
    {
        snapshot = null;
        if (value == null)
        {
            return false;
        }

        snapshot = new ItemDataEntrySnapshot
        {
            keyValue = (int)key,
            keyName = key.ToString(),
            valueType = value.GetType().AssemblyQualifiedName,
            hasValue = true
        };

        switch (value)
        {
            case IntItemData intData:
                snapshot.intValue = intData.Value;
                return true;
            case FloatItemData floatData:
                snapshot.floatValue = floatData.Value;
                return true;
            case BoolItemData boolData:
                snapshot.boolValue = boolData.Value;
                return true;
            case OptionableIntItemData optionableIntData:
                snapshot.hasValue = TryReadOptionableInt(optionableIntData, out int optionableIntValue);
                if (snapshot.hasValue)
                {
                    snapshot.intValue = optionableIntValue;
                }

                return true;
            case OptionableBoolItemData optionableBoolData:
                snapshot.hasValue = TryReadOptionableBool(optionableBoolData, out bool optionableBoolValue);
                if (snapshot.hasValue)
                {
                    snapshot.boolValue = optionableBoolValue;
                }

                return true;
            case ColorItemData colorData:
                snapshot.hasColorValue = true;
                snapshot.colorValue = new Vector4Snapshot(new Vector4(colorData.Value.r, colorData.Value.g, colorData.Value.b, colorData.Value.a));
                return true;
            case BackpackData backpackData:
                if (backpackData.itemSlots != null)
                {
                    for (int i = 0; i < backpackData.itemSlots.Length; i++)
                    {
                        snapshot.backpackSlots.Add(CaptureItemSlot(backpackData.itemSlots[i]));
                    }
                }

                return true;
            default:
                try
                {
                    snapshot.serializedJson = JsonConvert.SerializeObject(value, Formatting.None);
                    return true;
                }
                catch
                {
                    snapshot.serializedJson = value.ToString();
                    snapshot.stringValue = value.ToString();
                    return true;
                }
        }
    }

    private static void ApplyGenericItemData(ItemInstanceData data, List<ItemDataEntrySnapshot> snapshots)
    {
        if (data == null || snapshots == null || snapshots.Count == 0)
        {
            return;
        }

        for (int i = 0; i < snapshots.Count; i++)
        {
            ItemDataEntrySnapshot entry = snapshots[i];
            if (entry == null)
            {
                continue;
            }

            DataEntryKey key;
            if (!TryResolveDataEntryKey(entry, out key))
            {
                continue;
            }

            if (!TryCreateDataEntryValue(entry, out DataEntryValue value) || value == null)
            {
                continue;
            }

            try
            {
                data.data ??= new Dictionary<DataEntryKey, DataEntryValue>();
                data.data[key] = value;
            }
            catch
            {
                // Ignore incompatible entry restoration on game updates.
            }
        }
    }

    private static bool TryResolveDataEntryKey(ItemDataEntrySnapshot entry, out DataEntryKey key)
    {
        key = default;
        if (entry == null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(entry.keyName)
            && Enum.TryParse(entry.keyName, ignoreCase: true, out key))
        {
            return true;
        }

        key = (DataEntryKey)entry.keyValue;
        return true;
    }

    private static bool TryCreateDataEntryValue(ItemDataEntrySnapshot entry, out DataEntryValue value)
    {
        value = null;
        if (entry == null)
        {
            return false;
        }

        Type entryType = ResolveDataEntryValueType(entry.valueType);
        if (entryType == null || !typeof(DataEntryValue).IsAssignableFrom(entryType))
        {
            return false;
        }

        try
        {
            if (entryType == typeof(IntItemData))
            {
                value = new IntItemData { Value = entry.intValue ?? 0 };
                return true;
            }

            if (entryType == typeof(FloatItemData))
            {
                value = new FloatItemData { Value = entry.floatValue ?? 0f };
                return true;
            }

            if (entryType == typeof(BoolItemData))
            {
                value = new BoolItemData { Value = entry.boolValue ?? false };
                return true;
            }

            if (entryType == typeof(OptionableIntItemData))
            {
                value = new OptionableIntItemData
                {
                    HasData = entry.hasValue,
                    Value = entry.intValue ?? 0
                };
                return true;
            }

            if (entryType == typeof(OptionableBoolItemData))
            {
                value = new OptionableBoolItemData
                {
                    HasData = entry.hasValue,
                    Value = entry.boolValue ?? false
                };
                return true;
            }

            if (entryType == typeof(ColorItemData))
            {
                Vector4 color = entry.colorValue != null ? entry.colorValue.ToUnity() : Vector4.zero;
                value = new ColorItemData
                {
                    Value = new Color(color.x, color.y, color.z, entry.hasColorValue ? color.w : 1f)
                };
                return true;
            }

            if (entryType == typeof(BackpackData))
            {
                BackpackData backpackData = new BackpackData();
                backpackData.Init();
                if (entry.backpackSlots != null)
                {
                    int slotCount = backpackData.itemSlots != null ? backpackData.itemSlots.Length : 0;
                    for (int i = 0; i < entry.backpackSlots.Count && i < slotCount; i++)
                    {
                        ItemSlotSnapshot slotSnapshot = entry.backpackSlots[i];
                        if (slotSnapshot == null || !slotSnapshot.HasItem())
                        {
                            continue;
                        }

                        if (ItemDatabase.TryGetItem(slotSnapshot.itemId, out Item item) && item != null)
                        {
                            backpackData.AddItem(item, CreateItemInstanceData(slotSnapshot), (byte)i);
                        }
                    }
                }

                value = backpackData;
                return true;
            }

            if (string.IsNullOrWhiteSpace(entry.serializedJson))
            {
                return false;
            }

            object deserialized = JsonConvert.DeserializeObject(entry.serializedJson, entryType);
            value = deserialized as DataEntryValue;
            return value != null;
        }
        catch
        {
            return false;
        }
    }

    private static void SetItemDataEntry(ItemInstanceData data, DataEntryKey key, DataEntryValue value)
    {
        if (data == null || value == null)
        {
            return;
        }

        try
        {
            data.data ??= new Dictionary<DataEntryKey, DataEntryValue>();
            data.data[key] = value;
        }
        catch
        {
            // Ignore incompatible entry restoration on game updates.
        }
    }

    private static Type ResolveDataEntryValueType(string assemblyQualifiedTypeName)
    {
        if (string.IsNullOrWhiteSpace(assemblyQualifiedTypeName))
        {
            return null;
        }

        Type resolved = Type.GetType(assemblyQualifiedTypeName, throwOnError: false);
        if (resolved != null)
        {
            return resolved;
        }

        string typeName = assemblyQualifiedTypeName;
        int commaIndex = assemblyQualifiedTypeName.IndexOf(',');
        if (commaIndex >= 0)
        {
            typeName = assemblyQualifiedTypeName.Substring(0, commaIndex).Trim();
        }

        return AccessTools.TypeByName(typeName);
    }

    private void CaptureCampfires(List<CampfireSnapshot> output)
    {
        MapHandler mapHandler = UnityObject.FindFirstObjectByType<MapHandler>();
        if (mapHandler == null || mapHandler.segments == null)
        {
            return;
        }

        for (int i = 0; i < mapHandler.segments.Length; i++)
        {
            try
            {
                MapHandler.MapSegment segment = mapHandler.segments[i];
                if (segment == null || segment.segmentCampfire == null)
                {
                    continue;
                }

                Campfire campfire = segment.segmentCampfire.GetComponentInChildren<Campfire>(true);
                if (campfire == null)
                {
                    continue;
                }

                output.Add(new CampfireSnapshot
                {
                    segmentIndex = i,
                    campfireName = ((UnityObject)campfire).name,
                    state = (int)campfire.state,
                    beenBurningFor = campfire.beenBurningFor,
                    advanceToSegment = (int)campfire.advanceToSegment
                });
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Could not snapshot campfire for segment {i}: {ex.Message}");
            }
        }
    }

    private void CaptureLuggageStates(List<LuggageSnapshot> output)
    {
        if (output == null)
        {
            return;
        }

        Luggage[] luggageObjects = UnityObject.FindObjectsByType<Luggage>(FindObjectsSortMode.None);
        for (int i = 0; i < luggageObjects.Length; i++)
        {
            Luggage luggage = luggageObjects[i];
            if (luggage == null)
            {
                continue;
            }

            try
            {
                LuggageSnapshot snapshot = new LuggageSnapshot
                {
                    objectName = NormalizeObjectName(((UnityObject)luggage).name),
                    objectPath = BuildTransformPath(((Component)luggage).transform),
                    position = new Vector3Snapshot(((Component)luggage).transform.position),
                    state = GetLuggageState(luggage)
                };

                if (luggage is RespawnChest respawnChest)
                {
                    snapshot.isRespawnChest = true;
                    snapshot.respawnChestSpent = respawnChest.IsSpent;
                    snapshot.respawnChestRevivedPlayers = GetRespawnChestHasRevivedPlayers(respawnChest);
                }

                output.Add(snapshot);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to capture luggage state: {ex.Message}");
            }
        }
    }

    private void CaptureContainerStates(List<ContainerSnapshot> output)
    {
        if (output == null)
        {
            return;
        }

        output.Clear();

        Luggage[] luggageObjects = UnityObject.FindObjectsByType<Luggage>(FindObjectsSortMode.None);
        for (int i = 0; i < luggageObjects.Length; i++)
        {
            Luggage luggage = luggageObjects[i];
            if (luggage == null)
            {
                continue;
            }

            try
            {
                ContainerSnapshot snapshot = new ContainerSnapshot
                {
                    containerType = luggage.GetType().FullName,
                    objectName = NormalizeObjectName(((UnityObject)luggage).name),
                    objectPath = BuildTransformPath(((Component)luggage).transform),
                    position = new Vector3Snapshot(((Component)luggage).transform.position),
                    state = GetLuggageState(luggage)
                };

                if (luggage is RespawnChest respawnChest)
                {
                    snapshot.boolA = respawnChest.IsSpent;
                    snapshot.boolB = GetRespawnChestHasRevivedPlayers(respawnChest);
                }

                output.Add(snapshot);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to capture container state for '{((UnityObject)luggage).name}': {ex.Message}");
            }
        }

        MirageLuggage[] mirageLuggage = UnityObject.FindObjectsByType<MirageLuggage>(FindObjectsSortMode.None);
        for (int i = 0; i < mirageLuggage.Length; i++)
        {
            MirageLuggage mirage = mirageLuggage[i];
            if (mirage == null)
            {
                continue;
            }

            try
            {
                ContainerSnapshot snapshot = new ContainerSnapshot
                {
                    containerType = mirage.GetType().FullName,
                    objectName = NormalizeObjectName(((UnityObject)mirage).name),
                    objectPath = BuildTransformPath(((Component)mirage).transform),
                    position = new Vector3Snapshot(((Component)mirage).transform.position),
                    state = GetMirageLuggageVisualState(mirage),
                    boolA = ((Component)mirage).gameObject.activeSelf
                };

                output.Add(snapshot);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to capture MirageLuggage state: {ex.Message}");
            }
        }
    }

    private void CaptureGroundItems(List<GroundItemSnapshot> output)
    {
        if (output == null)
        {
            return;
        }

        Item[] sceneItems = UnityObject.FindObjectsByType<Item>(FindObjectsSortMode.None);
        for (int i = 0; i < sceneItems.Length; i++)
        {
            Item item = sceneItems[i];
            if (!IsGroundItemCandidate(item))
            {
                continue;
            }

            Transform transform = ((Component)item).transform;
            GroundItemSnapshot snapshot = new GroundItemSnapshot
            {
                itemId = item.itemID,
                objectName = NormalizeObjectName(((UnityObject)item).name),
                objectPath = BuildTransformPath(transform),
                position = new Vector3Snapshot(transform.position),
                rotation = new Vector3Snapshot(transform.eulerAngles),
                velocity = item.rig != null ? new Vector3Snapshot(item.rig.linearVelocity) : new Vector3Snapshot(Vector3.zero),
                angularVelocity = item.rig != null ? new Vector3Snapshot(item.rig.angularVelocity) : new Vector3Snapshot(Vector3.zero),
                isKinematic = item.rig != null && item.rig.isKinematic
            };

            if (TryGetItemInstanceData(item, out ItemInstanceData itemData))
            {
                CaptureItemUsageData(itemData, snapshot);
            }
            else if (ItemTotalUsesField != null)
            {
                try
                {
                    object rawUses = ItemTotalUsesField.GetValue(item);
                    if (rawUses is int totalUses && totalUses >= 0)
                    {
                        snapshot.itemUses = totalUses;
                    }
                }
                catch
                {
                    // Ignore usage fallback failures.
                }
            }

            output.Add(snapshot);
        }
    }

    private void CaptureWorldObjects(List<WorldObjectSnapshot> output)
    {
        if (output == null)
        {
            return;
        }

        ShittyPiton[] placedPitons = UnityObject.FindObjectsByType<ShittyPiton>(FindObjectsSortMode.None);
        if (placedPitons != null && placedPitons.Length > 0)
        {
            CaptureWorldObjectSnapshots(
                placedPitons,
                "Piton",
                output,
                (component, snapshot) => { }
            );
        }
        else
        {
            // Backward fallback for versions/builds where pitons still expose ClimbingSpikeComponent as the placed object.
            CaptureWorldObjectSnapshots(
                UnityObject.FindObjectsByType<ClimbingSpikeComponent>(FindObjectsSortMode.None),
                "PitonLegacy",
                output,
                (component, snapshot) => { }
            );
        }
        CaptureWorldObjectSnapshots(
            UnityObject.FindObjectsByType<RopeAnchor>(FindObjectsSortMode.None),
            "RopeAnchor",
            output,
            (component, snapshot) => snapshot.boolA = component.Ghost
        );
        CaptureWorldObjectSnapshots(
            UnityObject.FindObjectsByType<Rope>(FindObjectsSortMode.None),
            "Rope",
            output,
            (component, snapshot) =>
            {
                snapshot.boolA = component.antigrav;
                snapshot.floatA = component.Segments;
            }
        );
        CaptureWorldObjectSnapshots(
            UnityObject.FindObjectsByType<RopeAnchorWithRope>(FindObjectsSortMode.None),
            "RopeAnchorWithRope",
            output,
            (component, snapshot) =>
            {
                snapshot.floatA = component.ropeSegmentLength;
                snapshot.boolA = component.ropeInstance != null || component.rope != null;
            }
        );
        CaptureWorldObjectSnapshots(
            UnityObject.FindObjectsByType<ScoutCannon>(FindObjectsSortMode.None),
            "ScoutCannon",
            output,
            (component, snapshot) => snapshot.boolA = component.lit
        );
        CaptureWorldObjectSnapshots(
            FindNonSegmentCampfires(),
            "PortableStove",
            output,
            (component, snapshot) =>
            {
                snapshot.floatA = Mathf.Clamp((int)component.state, 0, 2);
                snapshot.boolA = component.state == Campfire.FireState.Lit;
                snapshot.floatB = Mathf.Max(0f, component.beenBurningFor);
            }
        );
        CaptureWorldObjectSnapshots(
            UnityObject.FindObjectsByType<MagicBeanVine>(FindObjectsSortMode.None),
            "MagicBeanVine",
            output,
            (component, snapshot) => snapshot.floatA = Mathf.Max(0f, GetMagicBeanVineCurrentLength(component))
        );
        CaptureWorldObjectSnapshots(
            UnityObject.FindObjectsByType<CloudFungus>(FindObjectsSortMode.None),
            "CloudFungus",
            output,
            (component, snapshot) =>
            {
                snapshot.boolA = GetCloudFungusAlreadyBroke(component);
                snapshot.floatA = Mathf.Max(0f, GetCloudFungusTimeAlive(component));
            }
        );
        CaptureWorldObjectSnapshots(
            UnityObject.FindObjectsByType<CheckpointFlag>(FindObjectsSortMode.None),
            "CheckpointFlag",
            output,
            CaptureCheckpointFlagState
        );
        CaptureWorldObjectSnapshots(
            UnityObject.FindObjectsByType<CheckpointConstructable>(FindObjectsSortMode.None),
            "CheckpointConstructable",
            output,
            (component, snapshot) => { }
        );
        CaptureWorldObjectSnapshots(
            FindBounceFungusObjects(),
            "BounceFungus",
            output,
            (component, snapshot) => { }
        );
        CaptureWorldObjectSnapshots(
            UnityObject.FindObjectsByType<ShelfShroom>(FindObjectsSortMode.None),
            "ShelfShroom",
            output,
            (component, snapshot) => { }
        );
        RopeShooter[] chainLaunchers = UnityObject
            .FindObjectsByType<RopeShooter>(FindObjectsSortMode.None)
            .Where(IsGroundRopeShooterCandidate)
            .ToArray();
        CaptureWorldObjectSnapshots(
            chainLaunchers,
            "ChainLauncher",
            output,
            (component, snapshot) =>
            {
                int ammo = GetChainLauncherAmmo(component);
                snapshot.boolA = ammo > 0;
                snapshot.floatA = ammo;
            }
        );
    }

    private static void CaptureWorldObjectSnapshots<T>(T[] components, string kind, List<WorldObjectSnapshot> output, Action<T, WorldObjectSnapshot> applyExtra)
        where T : Component
    {
        if (components == null || output == null)
        {
            return;
        }

        for (int i = 0; i < components.Length; i++)
        {
            T component = components[i];
            if (component == null)
            {
                continue;
            }

            Transform transform = component.transform;
            WorldObjectSnapshot snapshot = new WorldObjectSnapshot
            {
                kind = kind,
                objectName = NormalizeObjectName(((UnityObject)component).name),
                objectPath = BuildTransformPath(transform),
                position = new Vector3Snapshot(transform.position),
                rotation = new Vector3Snapshot(transform.eulerAngles)
            };

            applyExtra?.Invoke(component, snapshot);
            output.Add(snapshot);
        }
    }

    private static void CaptureCheckpointFlagState(CheckpointFlag flag, WorldObjectSnapshot snapshot)
    {
        if (flag == null || snapshot == null)
        {
            return;
        }

        float[] statuses = GetCheckpointStatuses(flag);
        if (statuses != null && statuses.Length > 0)
        {
            snapshot.floatListA = new List<float>(statuses.Length);
            for (int i = 0; i < statuses.Length; i++)
            {
                snapshot.floatListA.Add(Mathf.Max(0f, statuses[i]));
            }
        }

        Character planter = GetCheckpointPlanter(flag);
        if (planter == null)
        {
            return;
        }

        PhotonView planterView = GetPhotonView((Component)planter);
        RealtimePlayer owner = planterView != null ? planterView.Owner : null;
        if (owner != null)
        {
            snapshot.intA = owner.ActorNumber;
            snapshot.stringA = owner.NickName;
        }
        else
        {
            snapshot.stringA = NormalizeObjectName(((UnityObject)planter).name);
        }

        snapshot.stringB = BuildTransformPath(((Component)planter).transform);
    }

    private IEnumerator LoadSaveRoutine(string fullPath)
    {
        if (isLoading)
        {
            yield break;
        }

        isLoading = true;
        SetStatus("Loading save...", Color.cyan, 10f);
        try
        {
            IEnumerator inner = LoadSaveRoutineCore(fullPath);
            while (true)
            {
                object current;
                try
                {
                    if (!inner.MoveNext())
                    {
                        break;
                    }

                    current = inner.Current;
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Load failed for '{fullPath}': {ex}");
                    SetStatus(BuildLoadFailureStatus(ex), Color.red, 6f);
                    yield break;
                }

                yield return current;
            }
        }
        finally
        {
            ClearPendingSeedForLoad();
            isLoading = false;
            RefreshSaveFileList();
        }
    }

    private IEnumerator LoadSaveRoutineCore(string fullPath)
    {
        Logger.LogInfo($"Load requested from '{fullPath}'.");
        if (!TryReadSaveEnvelope(fullPath, out SaveEnvelope envelope, out string reason))
        {
            SetStatus("Incompatible save: " + reason, Color.yellow, 6f);
            yield break;
        }

        if (envelope.players.Count == 0)
        {
            SetStatus("Save file has no player snapshots.", Color.yellow, 6f);
            yield break;
        }

        if (PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient)
        {
            SetStatus("Only host can load shared saves.", Color.yellow, 4f);
            yield break;
        }

        string targetScene = ResolveTargetScene(envelope.metadata);
        bool saveNeedsRunScene = SaveNeedsNonAirportScene(envelope.metadata);
        if (string.IsNullOrWhiteSpace(targetScene) && saveNeedsRunScene)
        {
            if (IsInAirportScene())
            {
                SetStatus("Saved level is unavailable or mismatched for this version.", Color.yellow, 7f);
                yield break;
            }

            SetStatus("Saved level mismatch. Loading into current level.", Color.yellow, 5f);
        }

        bool needsSceneLoad = !string.IsNullOrEmpty(targetScene)
            && !string.Equals(SceneManager.GetActiveScene().name, targetScene, StringComparison.OrdinalIgnoreCase);

        if (envelope.metadata.levelSeed != 0)
        {
            SetPendingSeedForLoad(envelope.metadata.levelSeed);
            Logger.LogInfo($"Queued seed {PendingSeedForLoad.Value} for next level generation");
        }
        else
        {
            ClearPendingSeedForLoad();
        }

        if (needsSceneLoad)
        {
            yield return StartCoroutine(LoadSceneRoutine(targetScene, envelope.metadata.ascent));
            if (!lastSceneLoadSucceeded)
            {
                SetStatus($"Failed to load scene '{targetScene}'.", Color.red, 6f);
                yield break;
            }
        }
        else
        {
            Ascents.currentAscent = envelope.metadata.ascent;
        }

            int desiredMatchedPlayers = GetDesiredMatchedPlayerCount(envelope.players);
            float playerWaitTimeout = desiredMatchedPlayers > 1 ? 45f : 20f;
            yield return StartCoroutine(WaitForPlayersRoutine(envelope.players, desiredMatchedPlayers, playerWaitTimeout));
            int matchedPlayers = CountMatchedPlayers(envelope.players);
            if (matchedPlayers <= 0)
            {
                SetStatus("No matching players were found for this save.", Color.yellow, 6f);
                yield break;
            }

            if (matchedPlayers < desiredMatchedPlayers)
            {
                Logger.LogWarning($"Continuing load with {matchedPlayers}/{desiredMatchedPlayers} currently matched players ({CountSavedPlayers(envelope.players)} saved).");
            }

        if (MapHandler.Exists)
        {
            Segment targetSegment = ClampSegment(envelope.metadata.currentSegment);
            MapHandler mapHandler = UnityObject.FindFirstObjectByType<MapHandler>();
            Segment currentSegment = mapHandler != null ? mapHandler.GetCurrentSegment() : Segment.Beach;
            if (targetSegment != Segment.Beach && targetSegment != currentSegment)
            {
                MapHandler.JumpToSegment(targetSegment);
                yield return new WaitForSeconds(0.75f);
            }
        }

        yield return StartCoroutine(RestoreWorldInteractablesRoutine(envelope));

        int appliedCount = 0;
        foreach (PlayerSnapshot playerSnapshot in envelope.players)
        {
            GamePlayer target = FindPlayer(playerSnapshot);
            if (target == null)
            {
                Logger.LogWarning($"Could not find player for snapshot '{playerSnapshot.playerName}' (Actor: {playerSnapshot.actorNumber})");
                continue;
            }

            yield return StartCoroutine(ApplyPlayerSnapshotRoutine(target, playerSnapshot));
            appliedCount++;
        }

        if (appliedCount <= 0)
        {
            SetStatus("Load finished but no players were restored.", Color.yellow, 6f);
            yield break;
        }

        RestoreRunMetadata(envelope.metadata);
        Logger.LogInfo($"Load succeeded for '{fullPath}' ({appliedCount}/{envelope.players.Count} players).");
        if (appliedCount < envelope.players.Count)
        {
            SetStatus($"Loaded save for {appliedCount}/{envelope.players.Count} saved players.", Color.yellow, 5f);
        }
        else
        {
            SetStatus($"Loaded save successfully ({appliedCount}/{envelope.players.Count} players).", Color.green, 4f);
        }
    }

    private IEnumerator LoadSceneRoutine(string sceneName, int ascent)
    {
        lastSceneLoadSucceeded = false;
        Ascents.currentAscent = ascent;
        try
        {
            GameHandler.AddStatus<SceneSwitchingStatus>(new SceneSwitchingStatus());
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Could not set scene switching status: {ex.Message}");
        }

        bool usedLoadingScreen = false;
        try
        {
            LoadingScreenHandler handler = RetrievableResourceSingleton<LoadingScreenHandler>.Instance;
            if (handler != null)
            {
                handler.Load(
                    LoadingScreen.LoadingScreenType.Plane,
                    null,
                    handler.LoadSceneProcess(sceneName, networked: true, yieldForCharacterSpawn: true, extraYieldTimeOnEnd: 0.5f)
                );
                usedLoadingScreen = true;
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"LoadingScreenHandler load failed, using fallback load: {ex.Message}");
        }

        if (!usedLoadingScreen)
        {
            AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName);
            if (operation == null)
            {
                Logger.LogError($"Could not start fallback scene load for '{sceneName}'.");
                yield break;
            }

            float fallbackTimeout = 45f;
            while (!operation.isDone && fallbackTimeout > 0f)
            {
                fallbackTimeout -= Time.unscaledDeltaTime;
                yield return null;
            }
        }

        float timeout = 45f;
        while (timeout > 0f)
        {
            string activeScene = SceneManager.GetActiveScene().name;
            bool sceneMatches = string.Equals(activeScene, sceneName, StringComparison.OrdinalIgnoreCase);
            bool doneLoading = !usedLoadingScreen || !LoadingScreenHandler.loading;
            if (sceneMatches && doneLoading)
            {
                lastSceneLoadSucceeded = true;
                yield break;
            }

            timeout -= Time.unscaledDeltaTime;
            yield return null;
        }

        Logger.LogWarning($"Timed out waiting for scene '{sceneName}' to finish loading.");
    }

    private int CountMatchedPlayers(List<PlayerSnapshot> players)
    {
        if (players == null || players.Count == 0)
        {
            return 0;
        }

        GamePlayer[] found = UnityObject.FindObjectsByType<GamePlayer>(FindObjectsSortMode.None);
        if (found.Length == 0)
        {
            return 0;
        }

        int matched = 0;
        for (int i = 0; i < players.Count; i++)
        {
            PlayerSnapshot snapshot = players[i];
            bool hasMatch = found.Any(player =>
            {
                PhotonView pv = GetPhotonView((Component)player);
                if (pv == null)
                {
                    return false;
                }

                RealtimePlayer owner = pv.Owner;
                if (snapshot.actorNumber > 0 && owner != null && owner.ActorNumber == snapshot.actorNumber)
                {
                    return true;
                }

                return owner != null && string.Equals(owner.NickName, snapshot.playerName, StringComparison.OrdinalIgnoreCase);
            });

            if (hasMatch)
            {
                matched++;
            }
        }

        return matched;
    }

    private int CountSavedPlayers(List<PlayerSnapshot> players)
    {
        if (players == null || players.Count == 0)
        {
            return 0;
        }

        return players.Count(snapshot => snapshot != null);
    }

    private int GetDesiredMatchedPlayerCount(List<PlayerSnapshot> players)
    {
        int savedPlayerCount = CountSavedPlayers(players);
        if (savedPlayerCount <= 1)
        {
            return savedPlayerCount;
        }

        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient)
        {
            return 1;
        }

        int roomPlayers = CountCurrentRoomPlayers();
        if (roomPlayers <= 1)
        {
            return 1;
        }

        return Math.Min(savedPlayerCount, roomPlayers);
    }

    private int CountCurrentRoomPlayers()
    {
        if (PhotonNetwork.CurrentRoom?.Players == null)
        {
            return 0;
        }

        return PhotonNetwork.CurrentRoom.Players.Count(pair => pair.Value != null);
    }

    private IEnumerator WaitForPlayersRoutine(List<PlayerSnapshot> players, int desiredMatches, float timeoutSeconds)
    {
        if (desiredMatches <= 0)
        {
            yield break;
        }

        const float pollIntervalSeconds = 0.1f;
        const float settleWindowSeconds = 3f;
        float timeout = Mathf.Max(1f, timeoutSeconds);
        float settledForSeconds = 0f;
        int bestMatched = 0;

        while (timeout > 0f)
        {
            int matched = CountMatchedPlayers(players);
            if (matched > bestMatched)
            {
                bestMatched = matched;
                settledForSeconds = 0f;
                Logger.LogInfo($"Player match progress: {matched}/{desiredMatches}.");
            }
            else
            {
                settledForSeconds += pollIntervalSeconds;
            }

            if (matched >= desiredMatches)
            {
                yield break;
            }

            if (matched > 0 && settledForSeconds >= settleWindowSeconds)
            {
                Logger.LogInfo($"Proceeding with partial player match after waiting {settledForSeconds:0.0}s ({matched}/{desiredMatches}).");
                yield break;
            }

            timeout -= pollIntervalSeconds;
            yield return new WaitForSeconds(pollIntervalSeconds);
        }
    }

    private IEnumerator ApplyPlayerSnapshotRoutine(GamePlayer player, PlayerSnapshot snapshot)
    {
        Character character = player.character;
        if (character == null)
        {
            yield break;
        }

        ApplyInventory(player, character, snapshot.inventory);
        ApplyCharacterSnapshot(character, snapshot.character);

        Vector3 targetPosition = snapshot.position.ToUnity();
        PhotonView characterView = GetPhotonView((Component)character);
        RealtimePlayer owner = characterView != null ? characterView.Owner : null;
        if (characterView != null && owner != null)
        {
            characterView.RPC("WarpPlayerRPC", owner, targetPosition, false);
        }
        else
        {
            ((Component)character).transform.position = targetPosition;
        }

        if (characterView != null && characterView.IsMine)
        {
            ((Component)character).transform.eulerAngles = snapshot.rotation.ToUnity();
            character.data.lookValues = snapshot.character.lookValues.ToUnity();
            RecalculateLookDirectionsMethod?.Invoke(character, null);
        }
        else if (characterView == null)
        {
            ((Component)character).transform.eulerAngles = snapshot.rotation.ToUnity();
        }

        ApplyCharacterVelocity(character, snapshot.velocity, snapshot.angularVelocity);
        ApplyEquippedSelection(player, character, snapshot.inventory);
        yield return new WaitForSeconds(0.05f);
    }

    private void ApplyCharacterSnapshot(Character character, CharacterSnapshot snapshot)
    {
        if (character == null || snapshot == null)
        {
            return;
        }

        character.data.dead = snapshot.dead;
        character.data.passedOut = snapshot.passedOut;
        character.data.fullyPassedOut = snapshot.fullyPassedOut;
        character.data.isGrounded = snapshot.isGrounded;
        character.data.isClimbing = snapshot.isClimbing;
        character.data.isRopeClimbing = snapshot.isRopeClimbing;
        character.data.isVineClimbing = snapshot.isVineClimbing;
        character.data.isSprinting = snapshot.isSprinting;
        character.data.currentStamina = snapshot.currentStamina;
        character.SetExtraStamina(snapshot.extraStamina);
        character.data.sinceGrounded = snapshot.sinceGrounded;
        ApplyCharacterStatuses(character, snapshot.statuses);
        ApplyCharacterCheckpointFlags(character, snapshot.checkpointFlagPaths);
    }

    private static void ApplyCharacterStatuses(Character character, List<CharacterStatusSnapshot> statusSnapshots)
    {
        if (character == null || character.refs == null || character.refs.afflictions == null)
        {
            return;
        }

        CharacterAfflictions afflictions = character.refs.afflictions;
        afflictions.ClearAllStatus(excludeCurse: false);

        if (statusSnapshots == null || statusSnapshots.Count == 0)
        {
            afflictions.PushStatuses(null);
            return;
        }

        for (int i = 0; i < statusSnapshots.Count; i++)
        {
            CharacterStatusSnapshot statusSnapshot = statusSnapshots[i];
            if (statusSnapshot == null || string.IsNullOrWhiteSpace(statusSnapshot.statusType))
            {
                continue;
            }

            if (!Enum.TryParse(statusSnapshot.statusType, ignoreCase: true, out CharacterAfflictions.STATUSTYPE statusType))
            {
                continue;
            }

            if (SkippedSavedStatuses.Contains(statusType))
            {
                continue;
            }

            float amount = Mathf.Max(0f, statusSnapshot.amount);
            afflictions.SetStatus(statusType, amount, pushStatus: false);
        }

        afflictions.PushStatuses(null);
    }

    private static void ApplyCharacterCheckpointFlags(Character character, List<string> checkpointFlagPaths)
    {
        if (character == null || character.data == null || CharacterDataCheckpointFlagsField == null)
        {
            return;
        }

        try
        {
            object raw = CharacterDataCheckpointFlagsField.GetValue(character.data);
            if (!(raw is IList flagList))
            {
                return;
            }

            flagList.Clear();
            if (checkpointFlagPaths == null || checkpointFlagPaths.Count == 0)
            {
                return;
            }

            CheckpointFlag[] flags = UnityObject.FindObjectsByType<CheckpointFlag>(FindObjectsSortMode.None);
            Dictionary<string, CheckpointFlag> byPath = new Dictionary<string, CheckpointFlag>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < flags.Length; i++)
            {
                CheckpointFlag flag = flags[i];
                if (flag == null)
                {
                    continue;
                }

                string path = BuildTransformPath(((Component)flag).transform);
                if (!string.IsNullOrWhiteSpace(path) && !byPath.ContainsKey(path))
                {
                    byPath[path] = flag;
                }
            }

            for (int i = 0; i < checkpointFlagPaths.Count; i++)
            {
                string path = checkpointFlagPaths[i];
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                if (byPath.TryGetValue(path, out CheckpointFlag flag) && flag != null)
                {
                    flagList.Add(flag);
                }
            }
        }
        catch
        {
            // Ignore checkpoint ownership list restore failures.
        }
    }

    private static void ApplyCharacterVelocity(Character character, Vector3Snapshot velocitySnapshot, Vector3Snapshot angularVelocitySnapshot)
    {
        if (character == null || character.data == null)
        {
            return;
        }

        Vector3 velocity = velocitySnapshot != null ? velocitySnapshot.ToUnity() : Vector3.zero;
        Vector3 angularVelocity = angularVelocitySnapshot != null ? angularVelocitySnapshot.ToUnity() : Vector3.zero;

        character.data.avarageVelocity = velocity;
        character.data.avarageLastFrameVelocity = velocity;

        Rigidbody hipRig = character.refs != null && character.refs.hip != null ? character.refs.hip.Rig : null;
        if (hipRig == null)
        {
            return;
        }

        try
        {
            hipRig.linearVelocity = velocity;
            hipRig.angularVelocity = angularVelocity;
        }
        catch
        {
            // Ignore rigidbody velocity restore failures on game updates.
        }
    }

    private static void ApplyEquippedSelection(GamePlayer player, Character character, InventorySnapshot snapshot)
    {
        if (player == null || character == null || snapshot == null)
        {
            return;
        }

        CharacterItems items = character.refs != null ? character.refs.items : null;
        if (items == null)
        {
            return;
        }

        int? selectedSlot = null;
        if (snapshot.selectedSlotId.HasValue && IsValidMainSlot(player, snapshot.selectedSlotId.Value))
        {
            selectedSlot = snapshot.selectedSlotId.Value;
        }
        else if (snapshot.equippedMainSlotIndex.HasValue && IsValidMainSlot(player, snapshot.equippedMainSlotIndex.Value))
        {
            selectedSlot = snapshot.equippedMainSlotIndex.Value;
        }
        else if (snapshot.heldItemId != ushort.MaxValue && player.itemSlots != null)
        {
            for (int i = 0; i < player.itemSlots.Length; i++)
            {
                ItemSlot slot = player.itemSlots[i];
                if (slot != null && slot.prefab != null && slot.prefab.itemID == snapshot.heldItemId)
                {
                    selectedSlot = i;
                    break;
                }
            }
        }

        if (selectedSlot.HasValue)
        {
            TryEquipSlot(items, (byte)selectedSlot.Value);
        }

        if (snapshot.heldItemId != ushort.MaxValue && ItemDatabase.TryGetItem(snapshot.heldItemId, out Item heldItem) && heldItem != null)
        {
            character.data.currentItem = heldItem;
        }
    }

    private static bool IsValidMainSlot(GamePlayer player, int slotIndex)
    {
        return player != null
            && player.itemSlots != null
            && slotIndex >= 0
            && slotIndex < player.itemSlots.Length;
    }

    private static bool TryEquipSlot(CharacterItems items, byte slotId)
    {
        if (items == null || CharacterItemsEquipSlotMethod == null)
        {
            return false;
        }

        object optionableSlot = CreateOptionableByte(slotId, hasValue: true);
        if (optionableSlot == null)
        {
            return false;
        }

        try
        {
            CharacterItemsEquipSlotMethod.Invoke(items, new[] { optionableSlot });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static object CreateOptionableByte(byte value, bool hasValue)
    {
        if (OptionableByteConstructor == null)
        {
            return null;
        }

        try
        {
            return OptionableByteConstructor.Invoke(new object[] { value, hasValue ? (byte)1 : (byte)0 });
        }
        catch
        {
            return null;
        }
    }

    private static bool TryGetCharacterSelectedSlot(Character character, out int selectedSlot)
    {
        selectedSlot = -1;
        CharacterItems items = character != null && character.refs != null ? character.refs.items : null;
        if (items == null || CharacterItemsCurrentSelectedSlotField == null)
        {
            return false;
        }

        try
        {
            object optionableValue = CharacterItemsCurrentSelectedSlotField.GetValue(items);
            if (TryReadOptionableByte(optionableValue, out byte slot))
            {
                selectedSlot = slot;
                return true;
            }
        }
        catch
        {
            // Ignore slot-selection reflection failures.
        }

        return false;
    }

    private static bool TryReadOptionableByte(object optionableValue, out byte value)
    {
        value = 0;
        if (optionableValue == null)
        {
            return false;
        }

        if (optionableValue is Optionable<byte> typedOption)
        {
            if (!typedOption.IsSome)
            {
                return false;
            }

            value = typedOption.Value;
            return true;
        }

        try
        {
            if (OptionableByteHasValueField != null)
            {
                object rawHasValue = OptionableByteHasValueField.GetValue(optionableValue);
                bool hasValue = rawHasValue is byte byteFlag ? byteFlag > 0 : rawHasValue is bool boolFlag && boolFlag;
                if (!hasValue)
                {
                    return false;
                }
            }

            if (OptionableByteValueField != null)
            {
                object rawValue = OptionableByteValueField.GetValue(optionableValue);
                if (rawValue is byte byteValue)
                {
                    value = byteValue;
                    return true;
                }

                if (rawValue is int intValue && intValue >= 0 && intValue <= byte.MaxValue)
                {
                    value = (byte)intValue;
                    return true;
                }
            }
        }
        catch
        {
            // Ignore optionable reflection failures.
        }

        return false;
    }

    private void ApplyInventory(GamePlayer player, Character character, InventorySnapshot snapshot)
    {
        if (player == null || snapshot == null)
        {
            return;
        }

        if (player.itemSlots != null)
        {
            for (int i = 0; i < player.itemSlots.Length; i++)
            {
                player.itemSlots[i].EmptyOut();
            }
        }

        player.tempFullSlot?.EmptyOut();
        ClearBackpackSlot(player);
        if (player.backpackSlot != null)
        {
            player.backpackSlot.hasBackpack = false;
        }

        int mainSlotCount = player.itemSlots != null ? player.itemSlots.Length : 0;
        for (int i = 0; i < snapshot.mainSlots.Count && i < mainSlotCount; i++)
        {
            SetItemSlot(player.itemSlots[i], snapshot.mainSlots[i]);
        }

        SetItemSlot(player.tempFullSlot, snapshot.tempSlot);

        if (snapshot.hasBackpack && player.backpackSlot != null)
        {
            player.backpackSlot.hasBackpack = true;
            ItemInstanceData backpackInstanceData = CreateItemInstanceData();
            if (!SetBackpackSlotInstanceData(player, backpackInstanceData))
            {
                backpackInstanceData = GetBackpackSlotInstanceData(player);
            }

            if (TryEnsureBackpackData(backpackInstanceData, out BackpackData backpackData))
            {
                int backpackSlotCount = backpackData.itemSlots != null ? backpackData.itemSlots.Length : 0;
                for (int i = 0; i < snapshot.backpackSlots.Count && i < backpackSlotCount; i++)
                {
                    ItemSlotSnapshot backpackSlotSnapshot = snapshot.backpackSlots[i];
                    if (!backpackSlotSnapshot.HasItem())
                    {
                        continue;
                    }

                    Item item = default;
                    if (ItemDatabase.TryGetItem(backpackSlotSnapshot.itemId, out item) && item != null)
                    {
                        backpackData.AddItem(item, CreateItemInstanceData(backpackSlotSnapshot), (byte)i);
                    }
                }
            }
        }

        if (player.itemSlots != null)
        {
            InventorySyncData syncData = new InventorySyncData(player.itemSlots, player.backpackSlot, player.tempFullSlot);
            byte[] payload = IBinarySerializable.ToManagedArray(syncData);
            PhotonView playerView = GetPhotonView((Component)player);
            if (playerView != null)
            {
                playerView.RPC("SyncInventoryRPC", RpcTarget.All, payload, true);
            }
        }

        ApplyEquippedSelection(player, character, snapshot);
    }

    private static void SetItemSlot(ItemSlot slot, ItemSlotSnapshot snapshot)
    {
        if (slot == null || snapshot == null || !snapshot.HasItem())
        {
            return;
        }

        Item item = default;
        if (ItemDatabase.TryGetItem(snapshot.itemId, out item) && item != null)
        {
            slot.SetItem(item, CreateItemInstanceData(snapshot));
        }
    }

    private static ItemInstanceData CreateItemInstanceData()
    {
        ItemInstanceData data = new ItemInstanceData(Guid.NewGuid());
        ItemInstanceDataHandler.AddInstanceData(data);
        return data;
    }

    private static ItemInstanceData CreateItemInstanceData(ItemSlotSnapshot snapshot)
    {
        ItemInstanceData data = CreateItemInstanceData();
        ApplyItemUsageData(data, snapshot);
        return data;
    }

    private static void ApplyItemUsageData(ItemInstanceData data, ItemSlotSnapshot snapshot)
    {
        if (data == null || snapshot == null)
        {
            return;
        }

        ApplyGenericItemData(data, snapshot.dataEntries);

        int? itemUses = snapshot.itemUses;
        if (itemUses.HasValue)
        {
            SetItemDataEntry(data, DataEntryKey.ItemUses, new IntItemData { Value = Mathf.Max(0, itemUses.Value) });
        }

        if (snapshot.petterItemUses.HasValue)
        {
            SetItemDataEntry(data, DataEntryKey.PetterItemUses, new IntItemData { Value = Mathf.Max(0, snapshot.petterItemUses.Value) });
        }

        if (snapshot.useRemainingPercentage.HasValue)
        {
            SetItemDataEntry(data, DataEntryKey.UseRemainingPercentage, new FloatItemData { Value = Mathf.Clamp01(snapshot.useRemainingPercentage.Value) });
        }

        if (snapshot.used.HasValue)
        {
            SetItemDataEntry(data, DataEntryKey.Used, new BoolItemData { Value = snapshot.used.Value });
        }

        if (snapshot.fuel.HasValue)
        {
            SetItemDataEntry(data, DataEntryKey.Fuel, new FloatItemData { Value = Mathf.Max(0f, snapshot.fuel.Value) });
        }

        if (snapshot.cookedAmount.HasValue)
        {
            SetItemDataEntry(data, DataEntryKey.CookedAmount, new IntItemData { Value = Mathf.Max(0, snapshot.cookedAmount.Value) });
        }

        if (snapshot.flareActive.HasValue)
        {
            SetItemDataEntry(data, DataEntryKey.FlareActive, new BoolItemData { Value = snapshot.flareActive.Value });
        }

        if (snapshot.screamTime.HasValue)
        {
            SetItemDataEntry(data, DataEntryKey.ScreamTime, new FloatItemData { Value = Mathf.Max(0f, snapshot.screamTime.Value) });
        }

        if (snapshot.spawnedBees.HasValue)
        {
            SetItemDataEntry(data, DataEntryKey.SpawnedBees, new BoolItemData { Value = snapshot.spawnedBees.Value });
        }
    }

    private static bool TryEnsureBackpackData(ItemInstanceData instanceData, out BackpackData backpackData)
    {
        backpackData = null;
        if (instanceData == null)
        {
            return false;
        }

        if (instanceData.TryGetDataEntry(DataEntryKey.BackpackData, out backpackData) && backpackData != null)
        {
            if (backpackData.itemSlots == null || backpackData.itemSlots.Length == 0)
            {
                backpackData.Init();
            }

            return true;
        }

        backpackData = instanceData.RegisterNewEntry<BackpackData>(DataEntryKey.BackpackData);
        if (backpackData == null)
        {
            return false;
        }

        backpackData.Init();
        return true;
    }

    private void RestoreRunMetadata(SaveMetadata metadata)
    {
        if (metadata == null)
        {
            return;
        }

        try
        {
            DayNightManager dayNight = DayNightManager.instance ?? UnityObject.FindFirstObjectByType<DayNightManager>();
            if (dayNight != null)
            {
                if (metadata.runDay.HasValue)
                {
                    dayNight.dayCount = Mathf.Max(0, metadata.runDay.Value);
                }

                if (metadata.timeOfDay.HasValue)
                {
                    dayNight.setTimeOfDay(metadata.timeOfDay.Value);
                }

                string targetTimeString = !string.IsNullOrWhiteSpace(metadata.inGameTime)
                    ? metadata.inGameTime
                    : metadata.timeOfDay.HasValue
                        ? dayNight.FloatToTimeString(metadata.timeOfDay.Value)
                        : string.Empty;
                SetDayNightTimeString(dayNight, targetTimeString);

                dayNight.UpdateCycle();
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Failed to restore day/night metadata: {ex.Message}");
        }

        if (!metadata.runTimeSeconds.HasValue)
        {
            return;
        }

        try
        {
            RunManager runManager = RunManager.Instance ?? UnityObject.FindFirstObjectByType<RunManager>();
            if (runManager == null)
            {
                return;
            }

            float runTime = Mathf.Max(0f, metadata.runTimeSeconds.Value);
            bool timerActive = metadata.runTimerActive ?? true;

            if (RunManagerRpcSyncTimeMethod != null)
            {
                RunManagerRpcSyncTimeMethod.Invoke(runManager, new object[] { runTime, timerActive });
            }
            else
            {
                runManager.timeSinceRunStarted = runTime;
                RunManagerTimerActiveField?.SetValue(runManager, timerActive);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Failed to restore run timer metadata: {ex.Message}");
        }
    }

    private void RestoreCampfires(List<CampfireSnapshot> campfireSnapshots)
    {
        if (campfireSnapshots == null || campfireSnapshots.Count == 0)
        {
            return;
        }

        MapHandler mapHandler = UnityObject.FindFirstObjectByType<MapHandler>();
        if (mapHandler == null || mapHandler.segments == null)
        {
            return;
        }

        foreach (CampfireSnapshot snapshot in campfireSnapshots)
        {
            if (snapshot.segmentIndex < 0 || snapshot.segmentIndex >= mapHandler.segments.Length)
            {
                continue;
            }

            MapHandler.MapSegment segment = mapHandler.segments[snapshot.segmentIndex];
            if (segment == null || segment.segmentCampfire == null)
            {
                continue;
            }

            Campfire campfire = segment.segmentCampfire.GetComponentInChildren<Campfire>(true);
            if (campfire == null)
            {
                continue;
            }

            Campfire.FireState state = (Campfire.FireState)Mathf.Clamp(snapshot.state, 0, 2);
            campfire.state = state;
            campfire.beenBurningFor = Mathf.Max(0f, snapshot.beenBurningFor);
            campfire.advanceToSegment = ClampSegment(snapshot.advanceToSegment);

            switch (state)
            {
                case Campfire.FireState.Off:
                    if (campfire.fireParticles != null) campfire.fireParticles.Stop();
                    if (campfire.smokeParticlesLit != null) campfire.smokeParticlesLit.Stop();
                    if (campfire.smokeParticlesOff != null) campfire.smokeParticlesOff.Play();
                    break;
                case Campfire.FireState.Lit:
                    if (campfire.fireParticles != null) campfire.fireParticles.Play();
                    if (campfire.smokeParticlesOff != null) campfire.smokeParticlesOff.Stop();
                    if (campfire.smokeParticlesLit != null) campfire.smokeParticlesLit.Play();
                    break;
                case Campfire.FireState.Spent:
                    if (campfire.fireParticles != null) campfire.fireParticles.Stop();
                    if (campfire.smokeParticlesOff != null) campfire.smokeParticlesOff.Stop();
                    if (campfire.smokeParticlesLit != null) campfire.smokeParticlesLit.Stop();
                    CampfireHideLogsMethod?.Invoke(campfire, null);
                    break;
            }

            CampfireUpdateLitMethod?.Invoke(campfire, null);
        }
    }

    private IEnumerator RestoreWorldInteractablesRoutine(SaveEnvelope envelope)
    {
        if (envelope == null)
        {
            yield break;
        }

        RestoreCampfires(envelope.campfires);
        RestoreContainerStates(envelope.containerStates, envelope.luggageStates);
        RestoreWorldObjects(envelope.worldObjects, envelope.formatVersion);
        yield return new WaitForSeconds(0.2f);

        // Run a second pass after objects finish spawning to improve fidelity.
        RestoreCampfires(envelope.campfires);
        RestoreContainerStates(envelope.containerStates, envelope.luggageStates);
        RestoreWorldObjects(envelope.worldObjects, envelope.formatVersion);
        yield return new WaitForSeconds(1.1f);
        RestoreContainerStates(envelope.containerStates, envelope.luggageStates);
        RestoreWorldObjects(envelope.worldObjects, envelope.formatVersion);
        RestoreGroundItems(envelope.groundItems);
        RestoreWorldObjects(envelope.worldObjects, envelope.formatVersion);
    }

    private void RestoreContainerStates(List<ContainerSnapshot> containerSnapshots, List<LuggageSnapshot> legacyLuggageSnapshots)
    {
        if (containerSnapshots == null || containerSnapshots.Count == 0)
        {
            RestoreLuggageStates(legacyLuggageSnapshots);
            return;
        }

        Luggage[] sceneLuggage = UnityObject.FindObjectsByType<Luggage>(FindObjectsSortMode.None);
        List<Luggage> remainingLuggage = new List<Luggage>(sceneLuggage.Where(luggage => luggage != null));
        const float maxMatchDistanceSquared = 400f;

        for (int i = 0; i < containerSnapshots.Count; i++)
        {
            ContainerSnapshot snapshot = containerSnapshots[i];
            if (snapshot == null || !IsLuggageContainerSnapshot(snapshot))
            {
                continue;
            }

            Luggage matched = FindLuggageForContainerSnapshot(snapshot, remainingLuggage, maxMatchDistanceSquared)
                ?? FindLuggageForContainerSnapshot(snapshot, remainingLuggage, float.MaxValue);
            if (matched == null)
            {
                continue;
            }

            remainingLuggage.Remove(matched);
            ApplyContainerSnapshot(matched, snapshot);
        }

        MirageLuggage[] mirageObjects = UnityObject.FindObjectsByType<MirageLuggage>(FindObjectsSortMode.None);
        List<MirageLuggage> remainingMirages = new List<MirageLuggage>(mirageObjects.Where(mirage => mirage != null));
        for (int i = 0; i < containerSnapshots.Count; i++)
        {
            ContainerSnapshot snapshot = containerSnapshots[i];
            if (snapshot == null || !IsMirageContainerSnapshot(snapshot))
            {
                continue;
            }

            MirageLuggage matched = FindMirageForContainerSnapshot(snapshot, remainingMirages, maxMatchDistanceSquared)
                ?? FindMirageForContainerSnapshot(snapshot, remainingMirages, float.MaxValue);
            if (matched == null)
            {
                continue;
            }

            remainingMirages.Remove(matched);
            ApplyMirageContainerSnapshot(matched, snapshot);
        }
    }

    private void RestoreLuggageStates(List<LuggageSnapshot> luggageSnapshots)
    {
        if (luggageSnapshots == null || luggageSnapshots.Count == 0)
        {
            return;
        }

        Luggage[] sceneLuggage = UnityObject.FindObjectsByType<Luggage>(FindObjectsSortMode.None);
        if (sceneLuggage.Length == 0)
        {
            return;
        }

        List<Luggage> remaining = new List<Luggage>(sceneLuggage);
        const float maxMatchDistanceSquared = 400f;

        for (int i = 0; i < luggageSnapshots.Count; i++)
        {
            LuggageSnapshot snapshot = luggageSnapshots[i];
            if (snapshot == null)
            {
                continue;
            }

            Luggage matched = FindLuggageForSnapshot(snapshot, remaining, maxMatchDistanceSquared);
            if (matched == null)
            {
                matched = FindLuggageForSnapshot(snapshot, remaining, float.MaxValue);
                if (matched == null)
                {
                    continue;
                }
            }

            remaining.Remove(matched);
            ApplyLuggageSnapshot(matched, snapshot);
        }
    }

    private static bool IsLuggageContainerSnapshot(ContainerSnapshot snapshot)
    {
        if (snapshot == null)
        {
            return false;
        }

        string typeName = snapshot.containerType;
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return true;
        }

        if (typeName.Equals(typeof(Luggage).FullName, StringComparison.OrdinalIgnoreCase)
            || typeName.Equals(typeof(LuggageCursed).FullName, StringComparison.OrdinalIgnoreCase)
            || typeName.Equals(typeof(RespawnChest).FullName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return typeName.EndsWith(".Luggage", StringComparison.OrdinalIgnoreCase)
            || typeName.EndsWith(".LuggageCursed", StringComparison.OrdinalIgnoreCase)
            || typeName.EndsWith(".RespawnChest", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMirageContainerSnapshot(ContainerSnapshot snapshot)
    {
        if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.containerType))
        {
            return false;
        }

        return snapshot.containerType.Equals(typeof(MirageLuggage).FullName, StringComparison.OrdinalIgnoreCase)
            || snapshot.containerType.EndsWith(".MirageLuggage", StringComparison.OrdinalIgnoreCase);
    }

    private static Luggage FindLuggageForContainerSnapshot(ContainerSnapshot snapshot, List<Luggage> candidates, float maxDistanceSquared)
    {
        if (snapshot == null)
        {
            return null;
        }

        LuggageSnapshot legacy = new LuggageSnapshot
        {
            objectName = snapshot.objectName,
            objectPath = snapshot.objectPath,
            position = snapshot.position ?? new Vector3Snapshot(),
            state = snapshot.state,
            isRespawnChest = snapshot.boolA || snapshot.boolB
        };

        return FindLuggageForSnapshot(legacy, candidates, maxDistanceSquared);
    }

    private static MirageLuggage FindMirageForContainerSnapshot(ContainerSnapshot snapshot, List<MirageLuggage> candidates, float maxDistanceSquared)
    {
        if (snapshot == null || candidates == null || candidates.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.objectPath))
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                MirageLuggage candidate = candidates[i];
                if (candidate == null)
                {
                    continue;
                }

                string candidatePath = BuildTransformPath(((Component)candidate).transform);
                if (string.Equals(candidatePath, snapshot.objectPath, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }
        }

        MirageLuggage best = null;
        float bestDistance = maxDistanceSquared;
        Vector3 targetPosition = snapshot.position != null ? snapshot.position.ToUnity() : Vector3.zero;
        string targetName = NormalizeObjectName(snapshot.objectName);
        for (int i = 0; i < candidates.Count; i++)
        {
            MirageLuggage candidate = candidates[i];
            if (candidate == null)
            {
                continue;
            }

            string candidateName = NormalizeObjectName(((UnityObject)candidate).name);
            if (!string.IsNullOrWhiteSpace(targetName) && !string.Equals(candidateName, targetName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            float distanceSquared = (((Component)candidate).transform.position - targetPosition).sqrMagnitude;
            if (distanceSquared <= bestDistance)
            {
                bestDistance = distanceSquared;
                best = candidate;
            }
        }

        return best;
    }

    private static void ApplyContainerSnapshot(Luggage luggage, ContainerSnapshot snapshot)
    {
        if (luggage == null || snapshot == null)
        {
            return;
        }

        int targetState = Mathf.Max(0, snapshot.state);
        SetLuggageStateRaw(luggage, targetState);

        if (targetState > 0)
        {
            try
            {
                PhotonView view = GetPhotonView((Component)luggage);
                if (view != null)
                {
                    view.RPC("OpenLuggageRPC", RpcTarget.All, false);
                }
                else
                {
                    LuggageOpenRpcMethod?.Invoke(luggage, new object[] { false });
                }
            }
            catch
            {
                // Ignore open state restoration failures.
            }
        }

        if (luggage is RespawnChest respawnChest)
        {
            SetRespawnChestState(respawnChest, snapshot.boolA, snapshot.boolB);
        }
    }

    private static void ApplyMirageContainerSnapshot(MirageLuggage mirage, ContainerSnapshot snapshot)
    {
        if (mirage == null || snapshot == null)
        {
            return;
        }

        ((Component)mirage).gameObject.SetActive(snapshot.boolA);
        if (MirageLuggageSetStateMethod != null)
        {
            try
            {
                float visualState = Mathf.Clamp01(snapshot.state > 0 ? 1f : snapshot.floatA);
                MirageLuggageSetStateMethod.Invoke(mirage, new object[] { visualState });
            }
            catch
            {
                // Ignore mirage visual-state restoration failures.
            }
        }
    }

    private static Luggage FindClosestLuggage(Vector3 targetPosition, List<Luggage> candidates, float maxDistanceSquared)
    {
        Luggage best = null;
        float bestDistance = maxDistanceSquared;

        for (int i = 0; i < candidates.Count; i++)
        {
            Luggage candidate = candidates[i];
            if (candidate == null)
            {
                continue;
            }

            float distanceSquared = (((Component)candidate).transform.position - targetPosition).sqrMagnitude;
            if (distanceSquared <= bestDistance)
            {
                bestDistance = distanceSquared;
                best = candidate;
            }
        }

        return best;
    }

    private static Luggage FindLuggageForSnapshot(LuggageSnapshot snapshot, List<Luggage> candidates, float maxDistanceSquared)
    {
        if (snapshot == null || candidates == null || candidates.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.objectPath))
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                Luggage candidate = candidates[i];
                if (candidate == null)
                {
                    continue;
                }

                string candidatePath = BuildTransformPath(((Component)candidate).transform);
                if (string.Equals(candidatePath, snapshot.objectPath, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(snapshot.objectName))
        {
            Luggage bestByName = null;
            float bestDistanceByName = maxDistanceSquared;
            Vector3 targetPosition = snapshot.position.ToUnity();
            string snapshotName = NormalizeObjectName(snapshot.objectName);
            for (int i = 0; i < candidates.Count; i++)
            {
                Luggage candidate = candidates[i];
                if (candidate == null)
                {
                    continue;
                }

                string candidateName = NormalizeObjectName(((UnityObject)candidate).name);
                if (!string.IsNullOrWhiteSpace(snapshotName) && !string.Equals(candidateName, snapshotName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                float distanceSquared = (((Component)candidate).transform.position - targetPosition).sqrMagnitude;
                if (distanceSquared <= bestDistanceByName)
                {
                    bestDistanceByName = distanceSquared;
                    bestByName = candidate;
                }
            }

            if (bestByName != null)
            {
                return bestByName;
            }
        }

        return FindClosestLuggage(snapshot.position.ToUnity(), candidates, maxDistanceSquared);
    }

    private void ApplyLuggageSnapshot(Luggage luggage, LuggageSnapshot snapshot)
    {
        if (luggage == null || snapshot == null)
        {
            return;
        }

        int targetState = Mathf.Max(0, snapshot.state);
        SetLuggageStateRaw(luggage, targetState);

        if (targetState > 0)
        {
            try
            {
                PhotonView view = GetPhotonView((Component)luggage);
                if (view != null)
                {
                    view.RPC("OpenLuggageRPC", RpcTarget.All, false);
                }
                else
                {
                    LuggageOpenRpcMethod?.Invoke(luggage, new object[] { false });
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to restore luggage open state: {ex.Message}");
            }
        }

        if (snapshot.isRespawnChest && luggage is RespawnChest respawnChest)
        {
            SetRespawnChestState(respawnChest, snapshot.respawnChestSpent, snapshot.respawnChestRevivedPlayers);
        }
    }

    private void RestoreGroundItems(List<GroundItemSnapshot> itemSnapshots)
    {
        if (itemSnapshots == null)
        {
            return;
        }

        Item[] sceneItems = UnityObject.FindObjectsByType<Item>(FindObjectsSortMode.None);
        List<Item> candidates = new List<Item>(sceneItems.Where(IsGroundItemCandidate));

        for (int i = 0; i < itemSnapshots.Count; i++)
        {
            GroundItemSnapshot snapshot = itemSnapshots[i];
            if (snapshot == null || snapshot.itemId == ushort.MaxValue)
            {
                continue;
            }

            Item matched = FindGroundItemForSnapshot(snapshot, candidates, 16f);
            if (matched != null)
            {
                candidates.Remove(matched);
                ApplyGroundItemSnapshot(matched, snapshot);
                continue;
            }

            Item spawned = TrySpawnGroundItem(snapshot);
            if (spawned != null)
            {
                ApplyGroundItemSnapshot(spawned, snapshot);
            }
        }

        // Remove untracked ground items to avoid duplicate world loot after load.
        for (int i = 0; i < candidates.Count; i++)
        {
            Item item = candidates[i];
            if (item == null)
            {
                continue;
            }

            try
            {
                if (PhotonNetwork.InRoom)
                {
                    PhotonView view = GetPhotonView((Component)item);
                    if (view != null && view.ViewID > 0)
                    {
                        PhotonNetwork.Destroy(((Component)item).gameObject);
                        continue;
                    }
                }

                UnityObject.Destroy(((Component)item).gameObject);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to remove extra ground item: {ex.Message}");
            }
        }
    }

    private static Item FindGroundItemForSnapshot(GroundItemSnapshot snapshot, List<Item> candidates, float maxDistanceSquared)
    {
        if (snapshot == null || candidates == null || candidates.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.objectPath))
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                Item candidate = candidates[i];
                if (candidate == null || candidate.itemID != snapshot.itemId)
                {
                    continue;
                }

                string candidatePath = BuildTransformPath(((Component)candidate).transform);
                if (string.Equals(candidatePath, snapshot.objectPath, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }
        }

        Item best = null;
        float bestDistance = maxDistanceSquared;
        Vector3 targetPosition = snapshot.position.ToUnity();
        string targetName = NormalizeObjectName(snapshot.objectName);
        for (int i = 0; i < candidates.Count; i++)
        {
            Item candidate = candidates[i];
            if (candidate == null || candidate.itemID != snapshot.itemId)
            {
                continue;
            }

            string candidateName = NormalizeObjectName(((UnityObject)candidate).name);
            if (!string.IsNullOrWhiteSpace(targetName) && !string.Equals(candidateName, targetName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            float distanceSquared = (((Component)candidate).transform.position - targetPosition).sqrMagnitude;
            if (distanceSquared <= bestDistance)
            {
                bestDistance = distanceSquared;
                best = candidate;
            }
        }

        if (best != null)
        {
            return best;
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            Item candidate = candidates[i];
            if (candidate == null || candidate.itemID != snapshot.itemId)
            {
                continue;
            }

            float distanceSquared = (((Component)candidate).transform.position - targetPosition).sqrMagnitude;
            if (distanceSquared <= bestDistance)
            {
                bestDistance = distanceSquared;
                best = candidate;
            }
        }

        return best;
    }

    private static void ApplyGroundItemSnapshot(Item item, GroundItemSnapshot snapshot)
    {
        if (item == null || snapshot == null)
        {
            return;
        }

        Vector3 position = snapshot.position.ToUnity();
        Quaternion rotation = Quaternion.Euler(snapshot.rotation.ToUnity());
        Transform transform = ((Component)item).transform;
        transform.position = position;
        transform.rotation = rotation;

        try
        {
            item.SetKinematicNetworked(snapshot.isKinematic, position, rotation);
        }
        catch
        {
            if (item.rig != null)
            {
                item.rig.isKinematic = snapshot.isKinematic;
            }
        }

        if (item.rig != null)
        {
            try
            {
                item.rig.linearVelocity = snapshot.velocity != null ? snapshot.velocity.ToUnity() : Vector3.zero;
                item.rig.angularVelocity = snapshot.angularVelocity != null ? snapshot.angularVelocity.ToUnity() : Vector3.zero;
            }
            catch
            {
                // Ignore rigidbody velocity restore failures.
            }
        }

        ApplyItemUsageData(item, snapshot);
    }

    private static void ApplyItemUsageData(Item item, GroundItemSnapshot snapshot)
    {
        if (item == null || snapshot == null)
        {
            return;
        }

        ItemInstanceData data = EnsureItemInstanceData(item);
        if (data != null)
        {
            ApplyGenericItemData(data, snapshot.dataEntries);

            if (snapshot.itemUses.HasValue)
            {
                SetItemDataEntry(data, DataEntryKey.ItemUses, new IntItemData { Value = Mathf.Max(0, snapshot.itemUses.Value) });
            }

            if (snapshot.petterItemUses.HasValue)
            {
                SetItemDataEntry(data, DataEntryKey.PetterItemUses, new IntItemData { Value = Mathf.Max(0, snapshot.petterItemUses.Value) });
            }

            if (snapshot.useRemainingPercentage.HasValue)
            {
                SetItemDataEntry(data, DataEntryKey.UseRemainingPercentage, new FloatItemData { Value = Mathf.Clamp01(snapshot.useRemainingPercentage.Value) });
            }

            if (snapshot.used.HasValue)
            {
                SetItemDataEntry(data, DataEntryKey.Used, new BoolItemData { Value = snapshot.used.Value });
            }

            if (snapshot.fuel.HasValue)
            {
                SetItemDataEntry(data, DataEntryKey.Fuel, new FloatItemData { Value = Mathf.Max(0f, snapshot.fuel.Value) });
            }

            if (snapshot.cookedAmount.HasValue)
            {
                SetItemDataEntry(data, DataEntryKey.CookedAmount, new IntItemData { Value = Mathf.Max(0, snapshot.cookedAmount.Value) });
            }

            if (snapshot.flareActive.HasValue)
            {
                SetItemDataEntry(data, DataEntryKey.FlareActive, new BoolItemData { Value = snapshot.flareActive.Value });
            }

            if (snapshot.screamTime.HasValue)
            {
                SetItemDataEntry(data, DataEntryKey.ScreamTime, new FloatItemData { Value = Mathf.Max(0f, snapshot.screamTime.Value) });
            }

            if (snapshot.spawnedBees.HasValue)
            {
                SetItemDataEntry(data, DataEntryKey.SpawnedBees, new BoolItemData { Value = snapshot.spawnedBees.Value });
            }
        }

        if (snapshot.useRemainingPercentage.HasValue)
        {
            try
            {
                item.SetUseRemainingPercentage(snapshot.useRemainingPercentage.Value);
            }
            catch
            {
                // Ignore if this item does not expose use percentage behavior.
            }
        }

        if (snapshot.itemUses.HasValue && ItemTotalUsesField != null)
        {
            try
            {
                ItemTotalUsesField.SetValue(item, Mathf.Max(0, snapshot.itemUses.Value));
            }
            catch
            {
                // Ignore field restore failures on game updates.
            }
        }
    }

    private static bool TryGetItemInstanceData(Item item, out ItemInstanceData data)
    {
        data = null;
        if (item == null || ItemDataField == null)
        {
            return false;
        }

        try
        {
            data = ItemDataField.GetValue(item) as ItemInstanceData;
            return data != null;
        }
        catch
        {
            return false;
        }
    }

    private static ItemInstanceData EnsureItemInstanceData(Item item)
    {
        if (item == null)
        {
            return null;
        }

        if (TryGetItemInstanceData(item, out ItemInstanceData data))
        {
            return data;
        }

        data = CreateItemInstanceData();
        if (ItemDataField != null)
        {
            try
            {
                ItemDataField.SetValue(item, data);
            }
            catch
            {
                // Ignore reflection assignment failures on game updates.
            }
        }

        return data;
    }

    private Item TrySpawnGroundItem(GroundItemSnapshot snapshot)
    {
        try
        {
            Item itemPrefab = default;
            if (!ItemDatabase.TryGetItem(snapshot.itemId, out itemPrefab) || itemPrefab == null)
            {
                return null;
            }

            Vector3 position = snapshot.position.ToUnity();
            Quaternion rotation = Quaternion.Euler(snapshot.rotation.ToUnity());

            GameObject spawned = null;
            if (PhotonNetwork.InRoom)
            {
                string[] resourcePaths =
                {
                    "0_Items/" + itemPrefab.name,
                    itemPrefab.name
                };

                for (int i = 0; i < resourcePaths.Length && spawned == null; i++)
                {
                    try
                    {
                        spawned = PhotonNetwork.Instantiate(resourcePaths[i], position, rotation);
                    }
                    catch
                    {
                        // Try next path.
                    }
                }
            }

            if (spawned == null)
            {
                spawned = UnityObject.Instantiate(itemPrefab.gameObject, position, rotation);
            }

            return spawned != null ? spawned.GetComponent<Item>() : null;
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Failed to spawn ground item {snapshot?.itemId}: {ex.Message}");
            return null;
        }
    }

    private void RestoreWorldObjects(List<WorldObjectSnapshot> snapshots, int formatVersion)
    {
        if (snapshots == null || snapshots.Count == 0)
        {
            return;
        }

        RestoreWorldObjectType(snapshots, "Piton", UnityObject.FindObjectsByType<ShittyPiton>(FindObjectsSortMode.None), (component, snapshot) =>
        {
            ApplyTransformSnapshot(component.transform, snapshot.position, snapshot.rotation);
        });
        RestoreWorldObjectType(snapshots, "PitonLegacy", UnityObject.FindObjectsByType<ClimbingSpikeComponent>(FindObjectsSortMode.None), (component, snapshot) =>
        {
            ApplyTransformSnapshot(component.transform, snapshot.position, snapshot.rotation);
        });

        RestoreWorldObjectType(snapshots, "RopeAnchor", UnityObject.FindObjectsByType<RopeAnchor>(FindObjectsSortMode.None), (component, snapshot) =>
        {
            ApplyTransformSnapshot(component.transform, snapshot.position, snapshot.rotation);
            component.Ghost = snapshot.boolA;
        });

        RestoreWorldObjectType(snapshots, "Rope", UnityObject.FindObjectsByType<Rope>(FindObjectsSortMode.None), (component, snapshot) =>
        {
            ApplyTransformSnapshot(component.transform, snapshot.position, snapshot.rotation);
            component.antigrav = snapshot.boolA;
            if (snapshot.floatA > 0f)
            {
                component.Segments = snapshot.floatA;
            }
        });
        RestoreWorldObjectType(snapshots, "RopeAnchorWithRope", UnityObject.FindObjectsByType<RopeAnchorWithRope>(FindObjectsSortMode.None), (component, snapshot) =>
        {
            ApplyTransformSnapshot(component.transform, snapshot.position, snapshot.rotation);
            if (snapshot.floatA > 0f)
            {
                component.ropeSegmentLength = snapshot.floatA;
            }

            if (snapshot.boolA && component.ropeInstance == null)
            {
                component.SpawnRope();
            }
        });

        RestoreWorldObjectType(snapshots, "ScoutCannon", UnityObject.FindObjectsByType<ScoutCannon>(FindObjectsSortMode.None), (component, snapshot) =>
        {
            ApplyTransformSnapshot(component.transform, snapshot.position, snapshot.rotation);
            if (snapshot.boolA && !component.lit)
            {
                component.RPCA_Light();
            }
        }, removeExtras: false);
        RestoreWorldObjectType(snapshots, "PortableStove", FindNonSegmentCampfires(), (component, snapshot) =>
        {
            ApplyTransformSnapshot(component.transform, snapshot.position, snapshot.rotation);

            Campfire.FireState state = (Campfire.FireState)Mathf.Clamp(Mathf.RoundToInt(snapshot.floatA), 0, 2);
            component.state = state;
            component.beenBurningFor = Mathf.Max(0f, snapshot.floatB);

            switch (state)
            {
                case Campfire.FireState.Off:
                    if (component.fireParticles != null) component.fireParticles.Stop();
                    if (component.smokeParticlesLit != null) component.smokeParticlesLit.Stop();
                    if (component.smokeParticlesOff != null) component.smokeParticlesOff.Play();
                    break;
                case Campfire.FireState.Lit:
                    if (component.fireParticles != null) component.fireParticles.Play();
                    if (component.smokeParticlesOff != null) component.smokeParticlesOff.Stop();
                    if (component.smokeParticlesLit != null) component.smokeParticlesLit.Play();
                    break;
                case Campfire.FireState.Spent:
                    if (component.fireParticles != null) component.fireParticles.Stop();
                    if (component.smokeParticlesOff != null) component.smokeParticlesOff.Stop();
                    if (component.smokeParticlesLit != null) component.smokeParticlesLit.Stop();
                    CampfireHideLogsMethod?.Invoke(component, null);
                    break;
            }

            CampfireUpdateLitMethod?.Invoke(component, null);
        });
        RestoreWorldObjectType(snapshots, "MagicBeanVine", UnityObject.FindObjectsByType<MagicBeanVine>(FindObjectsSortMode.None), (component, snapshot) =>
        {
            ApplyTransformSnapshot(component.transform, snapshot.position, snapshot.rotation);

            if (snapshot.floatA > 0f)
            {
                float minLength = Mathf.Max(0f, GetMagicBeanVineInitialLength(component));
                float maxLength = GetMagicBeanVineMaxLength(component);
                if (maxLength <= 0f)
                {
                    maxLength = snapshot.floatA;
                }

                SetMagicBeanVineCurrentLength(component, Mathf.Clamp(snapshot.floatA, minLength, maxLength));
            }
        });
        RestoreWorldObjectType(snapshots, "CloudFungus", UnityObject.FindObjectsByType<CloudFungus>(FindObjectsSortMode.None), (component, snapshot) =>
        {
            ApplyTransformSnapshot(component.transform, snapshot.position, snapshot.rotation);
            SetCloudFungusAlreadyBroke(component, snapshot.boolA);
            SetCloudFungusTimeAlive(component, Mathf.Max(0f, snapshot.floatA));
        });
        RestoreWorldObjectType(snapshots, "CheckpointFlag", UnityObject.FindObjectsByType<CheckpointFlag>(FindObjectsSortMode.None), (component, snapshot) =>
        {
            ApplyCheckpointFlagSnapshot(component, snapshot);
        });
        RestoreWorldObjectType(snapshots, "CheckpointConstructable", UnityObject.FindObjectsByType<CheckpointConstructable>(FindObjectsSortMode.None), (component, snapshot) =>
        {
            ApplyTransformSnapshot(component.transform, snapshot.position, snapshot.rotation);
        });
        RestoreWorldObjectType(snapshots, "BounceFungus", FindBounceFungusObjects(), (component, snapshot) =>
        {
            ApplyTransformSnapshot(component.transform, snapshot.position, snapshot.rotation);
        });
        if (formatVersion >= 5)
        {
            RestoreWorldObjectType(snapshots, "ShelfShroom", UnityObject.FindObjectsByType<ShelfShroom>(FindObjectsSortMode.None), (component, snapshot) =>
            {
                ApplyTransformSnapshot(component.transform, snapshot.position, snapshot.rotation);
            });
        }

        RopeShooter[] chainLaunchers = UnityObject
            .FindObjectsByType<RopeShooter>(FindObjectsSortMode.None)
            .Where(IsGroundRopeShooterCandidate)
            .ToArray();
        RestoreWorldObjectType(snapshots, "ChainLauncher", chainLaunchers, (component, snapshot) =>
        {
            ApplyTransformSnapshot(component.transform, snapshot.position, snapshot.rotation);

            int ammo = snapshot.floatA > 0f
                ? Mathf.Max(0, Mathf.RoundToInt(snapshot.floatA))
                : (snapshot.boolA ? 1 : 0);
            SetChainLauncherAmmo(component, ammo);
        });
    }

    private static void RestoreShelfShrooms(List<WorldObjectSnapshot> snapshots, bool hasShelfSnapshotSupport)
    {
        if (!hasShelfSnapshotSupport)
        {
            return;
        }

        List<WorldObjectSnapshot> shelfSnapshots = snapshots
            .Where(snapshot => snapshot != null && string.Equals(snapshot.kind, "ShelfShroom", StringComparison.OrdinalIgnoreCase))
            .ToList();

        List<ShelfShroom> remaining = UnityObject.FindObjectsByType<ShelfShroom>(FindObjectsSortMode.None)
            .Where(component => component != null)
            .ToList();

        for (int i = 0; i < shelfSnapshots.Count; i++)
        {
            WorldObjectSnapshot snapshot = shelfSnapshots[i];
            ShelfShroom matched = FindWorldObjectForSnapshot(snapshot, remaining, 16f);
            if (matched == null)
            {
                continue;
            }

            remaining.Remove(matched);
            ApplyTransformSnapshot(matched.transform, snapshot.position, snapshot.rotation);
        }

        for (int i = 0; i < remaining.Count; i++)
        {
            ShelfShroom component = remaining[i];
            if (component == null)
            {
                continue;
            }

            try
            {
                GameObject gameObject = component.gameObject;
                if (PhotonNetwork.InRoom)
                {
                    PhotonView view = gameObject.GetComponent<PhotonView>();
                    if (view != null && view.ViewID > 0)
                    {
                        PhotonNetwork.Destroy(gameObject);
                        continue;
                    }
                }

                UnityObject.Destroy(gameObject);
            }
            catch
            {
                // Ignore shelf shroom cleanup failures on load.
            }
        }
    }

    private void RestoreWorldObjectType<T>(List<WorldObjectSnapshot> snapshots, string kind, T[] candidates, Action<T, WorldObjectSnapshot> applySnapshot, bool removeExtras = true)
        where T : Component
    {
        if (snapshots == null || applySnapshot == null)
        {
            return;
        }

        List<T> remainingCandidates = candidates != null ? new List<T>(candidates.Where(c => c != null)) : new List<T>();
        for (int i = 0; i < snapshots.Count; i++)
        {
            WorldObjectSnapshot snapshot = snapshots[i];
            if (snapshot == null || !string.Equals(snapshot.kind, kind, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            T matched = FindWorldObjectForSnapshot(snapshot, remainingCandidates, 16f);
            if (matched == null)
            {
                try
                {
                    matched = TrySpawnWorldObjectFromSnapshot<T>(snapshot);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"Failed to spawn world object '{kind}' from snapshot '{Safe(snapshot.objectName)}': {ex.Message}");
                    continue;
                }
            }

            if (matched == null)
            {
                continue;
            }

            remainingCandidates.Remove(matched);
            try
            {
                applySnapshot(matched, snapshot);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to restore world object '{kind}' from snapshot '{Safe(snapshot.objectName)}': {ex.Message}");
            }
        }

        if (removeExtras)
        {
            DestroyExtraTrackedObjects(remainingCandidates);
        }
    }

    private static void DestroyExtraTrackedObjects<T>(List<T> remainingCandidates)
        where T : Component
    {
        if (remainingCandidates == null || remainingCandidates.Count == 0)
        {
            return;
        }

        for (int i = 0; i < remainingCandidates.Count; i++)
        {
            T component = remainingCandidates[i];
            if (component == null)
            {
                continue;
            }

            TryDestroyTrackedComponent(component);
        }
    }

    private static void TryDestroyTrackedComponent(Component component)
    {
        if (component == null)
        {
            return;
        }

        try
        {
            GameObject gameObject = component.gameObject;
            if (PhotonNetwork.InRoom)
            {
                PhotonView view = gameObject.GetComponent<PhotonView>();
                if (view != null && view.ViewID > 0)
                {
                    PhotonNetwork.Destroy(gameObject);
                    return;
                }
            }

            UnityObject.Destroy(gameObject);
        }
        catch
        {
            // Ignore world-object cleanup failures.
        }
    }

    private void ApplyCheckpointFlagSnapshot(CheckpointFlag flag, WorldObjectSnapshot snapshot)
    {
        if (flag == null || snapshot == null)
        {
            return;
        }

        ApplyTransformSnapshot(((Component)flag).transform, snapshot.position, snapshot.rotation);

        if (snapshot.floatListA != null && snapshot.floatListA.Count > 0)
        {
            float[] statuses = new float[snapshot.floatListA.Count];
            for (int i = 0; i < snapshot.floatListA.Count; i++)
            {
                statuses[i] = Mathf.Max(0f, snapshot.floatListA[i]);
            }

            SetCheckpointStatuses(flag, statuses);
        }

        Character planter = FindCheckpointOwner(snapshot);
        if (planter != null)
        {
            SetCheckpointPlanter(flag, planter);
        }
    }

    private Character FindCheckpointOwner(WorldObjectSnapshot snapshot)
    {
        if (snapshot == null)
        {
            return null;
        }

        GamePlayer[] players = UnityObject.FindObjectsByType<GamePlayer>(FindObjectsSortMode.None);
        if (players == null || players.Length == 0)
        {
            return null;
        }

        for (int i = 0; i < players.Length; i++)
        {
            GamePlayer player = players[i];
            if (player == null || player.character == null)
            {
                continue;
            }

            PhotonView view = GetPhotonView((Component)player.character) ?? GetPhotonView((Component)player);
            RealtimePlayer owner = view != null ? view.Owner : null;
            if (snapshot.intA > 0 && owner != null && owner.ActorNumber == snapshot.intA)
            {
                return player.character;
            }
        }

        if (!string.IsNullOrWhiteSpace(snapshot.stringA))
        {
            for (int i = 0; i < players.Length; i++)
            {
                GamePlayer player = players[i];
                if (player == null || player.character == null)
                {
                    continue;
                }

                PhotonView view = GetPhotonView((Component)player.character) ?? GetPhotonView((Component)player);
                RealtimePlayer owner = view != null ? view.Owner : null;
                if (owner != null && string.Equals(owner.NickName, snapshot.stringA, StringComparison.OrdinalIgnoreCase))
                {
                    return player.character;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(snapshot.stringB))
        {
            for (int i = 0; i < players.Length; i++)
            {
                GamePlayer player = players[i];
                if (player == null || player.character == null)
                {
                    continue;
                }

                string characterPath = BuildTransformPath(((Component)player.character).transform);
                if (string.Equals(characterPath, snapshot.stringB, StringComparison.OrdinalIgnoreCase))
                {
                    return player.character;
                }
            }
        }

        return null;
    }

    private static T FindWorldObjectForSnapshot<T>(WorldObjectSnapshot snapshot, List<T> candidates, float maxDistanceSquared)
        where T : Component
    {
        if (snapshot == null || candidates == null || candidates.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.objectPath))
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                T candidate = candidates[i];
                if (candidate == null)
                {
                    continue;
                }

                string candidatePath = BuildTransformPath(candidate.transform);
                if (string.Equals(candidatePath, snapshot.objectPath, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }
        }

        T best = null;
        float bestDistance = maxDistanceSquared;
        Vector3 targetPosition = snapshot.position.ToUnity();
        string targetName = NormalizeObjectName(snapshot.objectName);
        for (int i = 0; i < candidates.Count; i++)
        {
            T candidate = candidates[i];
            if (candidate == null)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(targetName))
            {
                string candidateName = NormalizeObjectName(((UnityObject)candidate).name);
                if (!string.Equals(candidateName, targetName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            float distanceSquared = (candidate.transform.position - targetPosition).sqrMagnitude;
            if (distanceSquared <= bestDistance)
            {
                bestDistance = distanceSquared;
                best = candidate;
            }
        }

        return best;
    }

    private static T TrySpawnWorldObjectFromSnapshot<T>(WorldObjectSnapshot snapshot)
        where T : Component
    {
        if (snapshot == null || !PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient)
        {
            return null;
        }

        string prefabName = NormalizeObjectName(snapshot.objectName);
        if (string.IsNullOrWhiteSpace(prefabName))
        {
            return null;
        }

        Vector3 position = snapshot.position.ToUnity();
        Quaternion rotation = Quaternion.Euler(snapshot.rotation.ToUnity());
        string[] paths =
        {
            "0_Items/" + prefabName,
            prefabName
        };

        for (int i = 0; i < paths.Length; i++)
        {
            try
            {
                GameObject spawned = PhotonNetwork.Instantiate(paths[i], position, rotation);
                if (spawned == null)
                {
                    continue;
                }

                T component = spawned.GetComponent<T>();
                if (component != null)
                {
                    return component;
                }
            }
            catch
            {
                // Ignore and try next path.
            }
        }

        GameObject loadedPrefab = FindLoadedPrefabForComponent<T>(prefabName);
        if (loadedPrefab != null)
        {
            GameObject spawnedLocal = UnityObject.Instantiate(loadedPrefab, position, rotation);
            if (spawnedLocal != null)
            {
                T component = spawnedLocal.GetComponent<T>();
                if (component != null)
                {
                    return component;
                }
            }
        }

        return null;
    }

    private static void ApplyTransformSnapshot(Transform transform, Vector3Snapshot position, Vector3Snapshot rotation)
    {
        if (transform == null || position == null || rotation == null)
        {
            return;
        }

        transform.position = position.ToUnity();
        transform.eulerAngles = rotation.ToUnity();
    }

    private static GameObject FindLoadedPrefabForComponent<T>(string prefabName)
        where T : Component
    {
        if (string.IsNullOrWhiteSpace(prefabName))
        {
            return null;
        }

        string normalizedTarget = NormalizeObjectName(prefabName);
        GameObject exactMatch = null;
        GameObject partialMatch = null;
        GameObject[] loaded = Resources.FindObjectsOfTypeAll<GameObject>();
        for (int i = 0; i < loaded.Length; i++)
        {
            GameObject candidate = loaded[i];
            if (candidate == null || candidate.scene.IsValid())
            {
                continue;
            }

            if (candidate.GetComponent<T>() == null)
            {
                continue;
            }

            string candidateName = NormalizeObjectName(candidate.name);
            if (string.Equals(candidateName, normalizedTarget, StringComparison.OrdinalIgnoreCase))
            {
                exactMatch = candidate;
                break;
            }

            if (partialMatch == null && (candidateName.IndexOf(normalizedTarget, StringComparison.OrdinalIgnoreCase) >= 0
                || normalizedTarget.IndexOf(candidateName, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                partialMatch = candidate;
            }
        }

        return exactMatch ?? partialMatch;
    }

    private static bool TryGetBackpackData(GamePlayer player, out BackpackData backpackData)
    {
        backpackData = null;
        if (player == null)
        {
            return false;
        }

        ItemInstanceData instanceData = GetBackpackSlotInstanceData(player);
        if (instanceData == null || instanceData.data == null)
        {
            return false;
        }

        if (!instanceData.data.TryGetValue(DataEntryKey.BackpackData, out DataEntryValue dataValue))
        {
            return false;
        }

        backpackData = dataValue as BackpackData;
        return backpackData != null;
    }

    private static ItemInstanceData GetBackpackSlotInstanceData(GamePlayer player)
    {
        if (player == null || player.backpackSlot == null)
        {
            return null;
        }

        if (player.backpackSlot is ItemSlot typedSlot)
        {
            return typedSlot.data;
        }

        object rawSlot = player.backpackSlot;
        Type slotType = rawSlot.GetType();
        PropertyInfo dataProperty = slotType.GetProperty("data", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (dataProperty != null && typeof(ItemInstanceData).IsAssignableFrom(dataProperty.PropertyType))
        {
            try
            {
                return dataProperty.GetValue(rawSlot, null) as ItemInstanceData;
            }
            catch
            {
                // Ignore reflection failures.
            }
        }

        FieldInfo dataField = slotType.GetField("data", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (dataField != null && typeof(ItemInstanceData).IsAssignableFrom(dataField.FieldType))
        {
            try
            {
                return dataField.GetValue(rawSlot) as ItemInstanceData;
            }
            catch
            {
                // Ignore reflection failures.
            }
        }

        return null;
    }

    private static bool SetBackpackSlotInstanceData(GamePlayer player, ItemInstanceData instanceData)
    {
        if (player == null || player.backpackSlot == null)
        {
            return false;
        }

        if (player.backpackSlot is ItemSlot typedSlot)
        {
            typedSlot.data = instanceData;
            return true;
        }

        object rawSlot = player.backpackSlot;
        Type slotType = rawSlot.GetType();
        PropertyInfo dataProperty = slotType.GetProperty("data", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (dataProperty != null && dataProperty.CanWrite && typeof(ItemInstanceData).IsAssignableFrom(dataProperty.PropertyType))
        {
            try
            {
                dataProperty.SetValue(rawSlot, instanceData, null);
                return true;
            }
            catch
            {
                // Ignore reflection failures.
            }
        }

        FieldInfo dataField = slotType.GetField("data", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (dataField != null && typeof(ItemInstanceData).IsAssignableFrom(dataField.FieldType))
        {
            try
            {
                dataField.SetValue(rawSlot, instanceData);
                return true;
            }
            catch
            {
                // Ignore reflection failures.
            }
        }

        return false;
    }

    private static void ClearBackpackSlot(GamePlayer player)
    {
        if (player == null || player.backpackSlot == null)
        {
            return;
        }

        if (player.backpackSlot is ItemSlot typedSlot)
        {
            typedSlot.EmptyOut();
            return;
        }

        object rawSlot = player.backpackSlot;
        Type slotType = rawSlot.GetType();
        MethodInfo emptyOutMethod = slotType.GetMethod("EmptyOut", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
        if (emptyOutMethod != null)
        {
            try
            {
                emptyOutMethod.Invoke(rawSlot, null);
                return;
            }
            catch
            {
                // Ignore reflection failures.
            }
        }

        SetBackpackSlotInstanceData(player, null);
    }

    private GamePlayer FindPlayer(PlayerSnapshot snapshot)
    {
        GamePlayer[] players = UnityObject.FindObjectsByType<GamePlayer>(FindObjectsSortMode.None);
        if (snapshot.actorNumber > 0)
        {
            GamePlayer byActor = players.FirstOrDefault(p =>
            {
                PhotonView photonView = GetPhotonView((Component)p);
                RealtimePlayer owner = photonView != null ? photonView.Owner : null;
                return owner != null && owner.ActorNumber == snapshot.actorNumber;
            });

            if (byActor != null)
            {
                return byActor;
            }
        }

        return players.FirstOrDefault(p =>
        {
            PhotonView photonView = GetPhotonView((Component)p);
            RealtimePlayer owner = photonView != null ? photonView.Owner : null;
            return owner != null && string.Equals(owner.NickName, snapshot.playerName, StringComparison.OrdinalIgnoreCase);
        });
    }

    private void RefreshSaveFileList()
    {
        saveEntries.Clear();

        try
        {
            if (!EnsureSaveDirectoryReady(showStatus: false))
            {
                return;
            }

            IEnumerable<FileInfo> files = new DirectoryInfo(saveDirectory)
                .GetFiles(SaveFilePattern, SearchOption.TopDirectoryOnly)
                .OrderByDescending(file => file.LastWriteTimeUtc);

            foreach (FileInfo fileInfo in files)
            {
                saveEntries.Add(BuildSaveListEntry(fileInfo));
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to refresh save file list: {ex}");
            SetStatus(BuildSaveFailureStatus(ex), Color.red, 6f);
        }
    }

    private SaveListEntry BuildSaveListEntry(FileInfo fileInfo)
    {
        SaveListEntry entry = new SaveListEntry
        {
            fileName = fileInfo.Name,
            fullPath = fileInfo.FullName,
            fileTime = fileInfo.LastWriteTime,
            fileSize = fileInfo.Length,
            isCompatible = false,
            incompatibilityReason = "Unknown format"
        };

        try
        {
            string json = File.ReadAllText(fileInfo.FullName);
            JObject root = JObject.Parse(json);

            if (!string.Equals(root["magic"]?.Value<string>(), SaveEnvelope.SaveMagic, StringComparison.Ordinal))
            {
                if (root["names"] != null && root["posX"] != null)
                {
                    entry.incompatibilityReason = "Legacy save format (old/other plugin)";
                }
                else
                {
                    entry.incompatibilityReason = "Not a PEAK Save Manager file";
                }

                return entry;
            }

            int formatVersion = root["formatVersion"]?.Value<int>() ?? -1;
            if (formatVersion < 1 || formatVersion > SaveEnvelope.CurrentFormatVersion)
            {
                entry.incompatibilityReason = $"Unsupported format version: {formatVersion}";
                return entry;
            }

            SaveEnvelope envelope = root.ToObject<SaveEnvelope>();
            if (envelope == null || envelope.metadata == null || envelope.players == null)
            {
                entry.incompatibilityReason = "Missing required save data";
                return entry;
            }

            if (envelope.players.Count == 0)
            {
                entry.incompatibilityReason = "Save has no player snapshots";
                return entry;
            }

            entry.isCompatible = true;
            entry.incompatibilityReason = null;
            entry.metadata = envelope.metadata;
            entry.metadata.currentSegmentName = NormalizeSegmentName(entry.metadata.currentSegmentName);
            if (string.IsNullOrWhiteSpace(entry.metadata.saveName))
            {
                string fallbackName = fileInfo.Name;
                if (fallbackName.EndsWith(".peaksave.json", StringComparison.OrdinalIgnoreCase))
                {
                    fallbackName = fallbackName.Substring(0, fallbackName.Length - ".peaksave.json".Length);
                }
                else
                {
                    fallbackName = Path.GetFileNameWithoutExtension(fallbackName);
                }

                entry.metadata.saveName = fallbackName;
            }

            if (entry.metadata.playerCount <= 0)
            {
                entry.metadata.playerCount = envelope.players.Count;
            }

            if (!entry.metadata.levelNumber.HasValue)
            {
                entry.metadata.levelNumber = ParseLevelNumber(entry.metadata.levelName);
            }
        }
        catch (Exception ex)
        {
            entry.incompatibilityReason = "Corrupted JSON: " + ex.GetType().Name;
        }

        return entry;
    }

    private static bool TryReadSaveEnvelope(string fullPath, out SaveEnvelope envelope, out string reason)
    {
        envelope = null;
        reason = string.Empty;

        if (!File.Exists(fullPath))
        {
            reason = "File not found";
            return false;
        }

        try
        {
            string json = File.ReadAllText(fullPath);
            JObject root = JObject.Parse(json);

            if (!string.Equals(root["magic"]?.Value<string>(), SaveEnvelope.SaveMagic, StringComparison.Ordinal))
            {
                reason = "Wrong save format";
                return false;
            }

            int formatVersion = root["formatVersion"]?.Value<int>() ?? -1;
            if (formatVersion < 1 || formatVersion > SaveEnvelope.CurrentFormatVersion)
            {
                reason = $"Unsupported save format version {formatVersion}";
                return false;
            }

            envelope = root.ToObject<SaveEnvelope>();
            if (envelope == null || envelope.metadata == null || envelope.players == null)
            {
                reason = "Missing required save sections";
                return false;
            }

            if (envelope.players.Count == 0)
            {
                reason = "Save has no player snapshots";
                return false;
            }

            envelope.campfires ??= new List<CampfireSnapshot>();
            envelope.luggageStates ??= new List<LuggageSnapshot>();
            envelope.containerStates ??= new List<ContainerSnapshot>();
            envelope.groundItems ??= new List<GroundItemSnapshot>();
            envelope.worldObjects ??= new List<WorldObjectSnapshot>();
            envelope.metadata.currentSegmentName = NormalizeSegmentName(envelope.metadata.currentSegmentName);

            for (int i = 0; i < envelope.players.Count; i++)
            {
                PlayerSnapshot playerSnapshot = envelope.players[i];
                if (playerSnapshot == null)
                {
                    continue;
                }

                playerSnapshot.velocity ??= new Vector3Snapshot();
                playerSnapshot.angularVelocity ??= new Vector3Snapshot();
                playerSnapshot.character ??= new CharacterSnapshot();
                playerSnapshot.character.checkpointFlagPaths ??= new List<string>();
                playerSnapshot.character.statuses ??= new List<CharacterStatusSnapshot>();
                playerSnapshot.inventory ??= new InventorySnapshot();
                playerSnapshot.inventory.mainSlots ??= new List<ItemSlotSnapshot>();
                playerSnapshot.inventory.backpackSlots ??= new List<ItemSlotSnapshot>();
                playerSnapshot.inventory.tempSlot ??= ItemSlotSnapshot.Empty();

                NormalizeSlotData(playerSnapshot.inventory.tempSlot);
                for (int slotIndex = 0; slotIndex < playerSnapshot.inventory.mainSlots.Count; slotIndex++)
                {
                    NormalizeSlotData(playerSnapshot.inventory.mainSlots[slotIndex]);
                }

                for (int slotIndex = 0; slotIndex < playerSnapshot.inventory.backpackSlots.Count; slotIndex++)
                {
                    NormalizeSlotData(playerSnapshot.inventory.backpackSlots[slotIndex]);
                }
            }

            for (int i = 0; i < envelope.groundItems.Count; i++)
            {
                GroundItemSnapshot groundSnapshot = envelope.groundItems[i];
                if (groundSnapshot == null)
                {
                    continue;
                }

                groundSnapshot.velocity ??= new Vector3Snapshot();
                groundSnapshot.angularVelocity ??= new Vector3Snapshot();
                groundSnapshot.dataEntries ??= new List<ItemDataEntrySnapshot>();
            }

            for (int i = 0; i < envelope.containerStates.Count; i++)
            {
                ContainerSnapshot containerSnapshot = envelope.containerStates[i];
                if (containerSnapshot == null)
                {
                    continue;
                }

                containerSnapshot.position ??= new Vector3Snapshot();
            }

            for (int i = 0; i < envelope.luggageStates.Count; i++)
            {
                LuggageSnapshot luggageSnapshot = envelope.luggageStates[i];
                if (luggageSnapshot == null)
                {
                    continue;
                }

                luggageSnapshot.position ??= new Vector3Snapshot();
            }

            for (int i = 0; i < envelope.worldObjects.Count; i++)
            {
                WorldObjectSnapshot worldSnapshot = envelope.worldObjects[i];
                if (worldSnapshot == null)
                {
                    continue;
                }

                worldSnapshot.floatListA ??= new List<float>();
            }

            return true;
        }
        catch (JsonException)
        {
            reason = "Corrupted JSON";
            return false;
        }
        catch (Exception ex)
        {
            reason = ex.GetType().Name;
            return false;
        }
    }

    private static void NormalizeSlotData(ItemSlotSnapshot snapshot)
    {
        if (snapshot == null)
        {
            return;
        }

        snapshot.dataEntries ??= new List<ItemDataEntrySnapshot>();
    }

    private void TryDeleteSave(string fullPath)
    {
        try
        {
            File.Delete(fullPath);
            RefreshSaveFileList();
            SetStatus("Deleted save file.", Color.yellow, 2.5f);
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to delete save file '{fullPath}': {ex}");
            SetStatus("Delete failed.", Color.red, 3f);
        }
    }

    private static string ResolveTargetScene(SaveMetadata metadata)
    {
        if (metadata == null)
        {
            return string.Empty;
        }

        string activeScene = SceneManager.GetActiveScene().name;
        HashSet<string> candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddSceneCandidate(candidates, metadata.levelName);
        if (metadata.levelNumber.HasValue)
        {
            AddSceneCandidate(candidates, $"Level_{metadata.levelNumber.Value}");
        }

        if (metadata.dailyLevelIndex.HasValue)
        {
            try
            {
                string mapBakerLevel = SingletonAsset<MapBaker>.Instance.GetLevel(metadata.dailyLevelIndex.Value + NextLevelService.debugLevelIndexOffset);
                AddSceneCandidate(candidates, mapBakerLevel);
            }
            catch
            {
                // ignored: fallback candidates are already added.
            }
        }

        AddSceneCandidate(candidates, metadata.sceneName);

        foreach (string candidate in candidates)
        {
            if (CanUseSceneCandidate(candidate, activeScene))
            {
                return candidate;
            }
        }

        return string.Empty;
    }

    private static bool SaveNeedsNonAirportScene(SaveMetadata metadata)
    {
        if (metadata == null)
        {
            return false;
        }

        if (metadata.levelNumber.HasValue && metadata.levelNumber.Value > 0)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(metadata.levelName) && !metadata.levelName.Equals("Airport", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(metadata.sceneName) && !metadata.sceneName.Equals("Airport", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return metadata.currentSegment > (int)Segment.Beach;
    }

    private static void AddSceneCandidate(HashSet<string> output, string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            return;
        }

        if (sceneName.Equals("Airport", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        output.Add(sceneName);
    }

    private static bool CanUseSceneCandidate(string sceneName, string activeScene)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            return false;
        }

        if (string.Equals(sceneName, activeScene, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return Application.CanStreamedLevelBeLoaded(sceneName);
    }

    private static bool IsInAirportScene()
    {
        string sceneName = SceneManager.GetActiveScene().name;
        return string.Equals(sceneName, "Airport", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveLevelName(string sceneName, int? dailyLevelIndex)
    {
        if (!string.IsNullOrWhiteSpace(sceneName) && sceneName.StartsWith("Level_", StringComparison.OrdinalIgnoreCase))
        {
            return sceneName;
        }

        if (!dailyLevelIndex.HasValue)
        {
            return sceneName;
        }

        try
        {
            return SingletonAsset<MapBaker>.Instance.GetLevel(dailyLevelIndex.Value + NextLevelService.debugLevelIndexOffset);
        }
        catch
        {
            return sceneName;
        }
    }

    private static int? ParseLevelNumber(string sceneOrLevelName)
    {
        if (string.IsNullOrWhiteSpace(sceneOrLevelName))
        {
            return null;
        }

        const string prefix = "Level_";
        if (!sceneOrLevelName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string numericPart = sceneOrLevelName.Substring(prefix.Length);
        if (int.TryParse(numericPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out int levelNumber))
        {
            return levelNumber;
        }

        return null;
    }

    private static Segment ClampSegment(int raw)
    {
        if (raw < 0)
        {
            return Segment.Beach;
        }

        if (raw > (int)Segment.Peak)
        {
            return Segment.Peak;
        }

        return (Segment)raw;
    }

    private static Vector3 GetCharacterPosition(Character character)
    {
        if (character == null)
        {
            return Vector3.zero;
        }

        if (character.Center != Vector3.zero)
        {
            return character.Center;
        }

        if (character.refs != null && character.refs.hip != null && character.refs.hip.Rig != null)
        {
            return character.refs.hip.Rig.position;
        }

        return ((Component)character).transform.position;
    }

    private static string SanitizeFileName(string value)
    {
        string fallback = string.IsNullOrWhiteSpace(value) ? "save" : value.Trim();
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            fallback = fallback.Replace(c, '_');
        }

        return fallback;
    }

    private static string BuildTransformPath(Transform transform)
    {
        if (transform == null)
        {
            return string.Empty;
        }

        StringBuilder builder = new StringBuilder(128);
        while (transform != null)
        {
            if (builder.Length == 0)
            {
                builder.Insert(0, transform.name);
            }
            else
            {
                builder.Insert(0, '/');
                builder.Insert(0, transform.name);
            }

            transform = transform.parent;
        }

        return builder.ToString();
    }

    internal static string ToDisplaySegmentName(string segmentName)
    {
        return ToDisplaySegmentName(segmentName, string.Empty);
    }

    internal static string ToDisplaySegmentName(string segmentName, string biomeId)
    {
        string normalizedSegment = NormalizeSegmentName(segmentName);
        string normalizedBiome = NormalizeBiomeDisplayName(biomeId, normalizedSegment);
        if (!string.IsNullOrWhiteSpace(normalizedBiome))
        {
            return normalizedBiome;
        }

        if (string.IsNullOrWhiteSpace(normalizedSegment))
        {
            return "-";
        }

        if (normalizedSegment.Equals("Beach", StringComparison.OrdinalIgnoreCase)
            || normalizedSegment.Equals("Shore", StringComparison.OrdinalIgnoreCase))
        {
            return "Shore";
        }

        if (normalizedSegment.Equals("Tropics", StringComparison.OrdinalIgnoreCase))
        {
            return "Tropics/Roots";
        }

        if (normalizedSegment.Equals("Alpine", StringComparison.OrdinalIgnoreCase))
        {
            return "Alpine/Mesa";
        }

        return normalizedSegment;
    }

    private static string NormalizeSegmentName(string segmentName)
    {
        if (string.IsNullOrWhiteSpace(segmentName))
        {
            return string.Empty;
        }

        string normalized = segmentName.Trim();
        if (normalized.Equals("Tropics/Roots", StringComparison.OrdinalIgnoreCase))
        {
            return "Tropics";
        }

        if (normalized.Equals("Alpine/Mesa", StringComparison.OrdinalIgnoreCase))
        {
            return "Alpine";
        }

        return normalized;
    }

    private static string NormalizeBiomeDisplayName(string biomeId, string normalizedSegment)
    {
        if (string.IsNullOrWhiteSpace(biomeId))
        {
            return string.Empty;
        }

        string normalized = biomeId.Trim();
        if (normalized.StartsWith("Biome_", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.Substring("Biome_".Length);
        }

        if (normalized.IndexOf("Roots", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Roots";
        }

        if (normalized.IndexOf("Tropics", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Tropics";
        }

        if (normalized.IndexOf("Alpine", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Alpine";
        }

        if (normalized.IndexOf("Mesa", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Mesa";
        }

        string compact = normalized
            .Replace("_", string.Empty)
            .Replace("-", string.Empty)
            .Trim();
        if (compact.Length >= 4)
        {
            int biomeCodeIndex = GetBiomeCodeIndexForSegment(normalizedSegment);
            if (biomeCodeIndex >= 0 && biomeCodeIndex < compact.Length)
            {
                if (TryGetBiomeNameFromCode(compact[biomeCodeIndex], out string biomeName))
                {
                    return biomeName;
                }
            }
        }

        return string.Empty;
    }

    private static int GetBiomeCodeIndexForSegment(string normalizedSegment)
    {
        if (string.IsNullOrWhiteSpace(normalizedSegment))
        {
            return -1;
        }

        if (normalizedSegment.Equals("Beach", StringComparison.OrdinalIgnoreCase)
            || normalizedSegment.Equals("Shore", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (normalizedSegment.Equals("Tropics", StringComparison.OrdinalIgnoreCase)
            || normalizedSegment.Equals("Roots", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (normalizedSegment.Equals("Alpine", StringComparison.OrdinalIgnoreCase)
            || normalizedSegment.Equals("Mesa", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        if (normalizedSegment.Equals("Volcano", StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        if (normalizedSegment.Equals("Peak", StringComparison.OrdinalIgnoreCase))
        {
            return 4;
        }

        return -1;
    }

    private static bool TryGetBiomeNameFromCode(char code, out string biomeName)
    {
        biomeName = string.Empty;
        switch (char.ToUpperInvariant(code))
        {
            case 'S':
                biomeName = "Shore";
                return true;
            case 'T':
                biomeName = "Tropics";
                return true;
            case 'R':
                biomeName = "Roots";
                return true;
            case 'A':
                biomeName = "Alpine";
                return true;
            case 'M':
                biomeName = "Mesa";
                return true;
            case 'V':
                biomeName = "Volcano";
                return true;
            case 'P':
                biomeName = "Peak";
                return true;
            default:
                return false;
        }
    }

    private static string NormalizeObjectName(string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName))
        {
            return string.Empty;
        }

        const string cloneSuffix = "(Clone)";
        string normalized = objectName.Trim();
        if (normalized.EndsWith(cloneSuffix, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.Substring(0, normalized.Length - cloneSuffix.Length).TrimEnd();
        }

        return normalized;
    }

    private static bool IsGroundItemCandidate(Item item)
    {
        if (item == null || !((Component)item).gameObject.activeInHierarchy)
        {
            return false;
        }

        if (item.itemState == ItemState.InBackpack)
        {
            return false;
        }

        if (item.holderCharacter != null || item.trueHolderCharacter != null)
        {
            return false;
        }

        Transform transform = ((Component)item).transform;
        if (transform == null)
        {
            return false;
        }

        if (transform.GetComponentInParent<GamePlayer>() != null || transform.GetComponentInParent<Character>() != null)
        {
            return false;
        }

        return true;
    }

    private static float GetMagicBeanVineCurrentLength(MagicBeanVine vine)
    {
        return GetFloatFieldValue(vine, MagicBeanVineCurrentLengthField, 0f);
    }

    private static float GetMagicBeanVineInitialLength(MagicBeanVine vine)
    {
        return GetFloatFieldValue(vine, MagicBeanVineInitialLengthField, 0f);
    }

    private static float GetMagicBeanVineMaxLength(MagicBeanVine vine)
    {
        return GetFloatFieldValue(vine, MagicBeanVineMaxLengthField, 0f);
    }

    private static void SetMagicBeanVineCurrentLength(MagicBeanVine vine, float value)
    {
        SetFloatFieldValue(vine, MagicBeanVineCurrentLengthField, Mathf.Max(0f, value));
    }

    private static bool GetCloudFungusAlreadyBroke(CloudFungus fungus)
    {
        return GetBoolFieldValue(fungus, CloudFungusAlreadyBrokeField, false);
    }

    private static float GetCloudFungusTimeAlive(CloudFungus fungus)
    {
        return GetFloatFieldValue(fungus, CloudFungusTimeAliveField, 0f);
    }

    private static void SetCloudFungusAlreadyBroke(CloudFungus fungus, bool value)
    {
        SetBoolFieldValue(fungus, CloudFungusAlreadyBrokeField, value);
    }

    private static void SetCloudFungusTimeAlive(CloudFungus fungus, float value)
    {
        SetFloatFieldValue(fungus, CloudFungusTimeAliveField, Mathf.Max(0f, value));
    }

    private static float[] GetCheckpointStatuses(CheckpointFlag flag)
    {
        if (flag == null)
        {
            return Array.Empty<float>();
        }

        if (CheckpointFlagStatusesField != null)
        {
            try
            {
                if (CheckpointFlagStatusesField.GetValue(flag) is float[] statuses && statuses != null)
                {
                    return statuses;
                }
            }
            catch
            {
                // Ignore reflection read failures.
            }
        }

        return Array.Empty<float>();
    }

    private static void SetCheckpointStatuses(CheckpointFlag flag, float[] statuses)
    {
        if (flag == null || CheckpointFlagStatusesField == null || statuses == null)
        {
            return;
        }

        try
        {
            CheckpointFlagStatusesField.SetValue(flag, statuses);
        }
        catch
        {
            // Ignore reflection write failures.
        }
    }

    private static Character GetCheckpointPlanter(CheckpointFlag flag)
    {
        if (flag == null)
        {
            return null;
        }

        if (CheckpointFlagPlanterField != null)
        {
            try
            {
                return CheckpointFlagPlanterField.GetValue(flag) as Character;
            }
            catch
            {
                // Ignore reflection failures.
            }
        }

        return null;
    }

    private static void SetCheckpointPlanter(CheckpointFlag flag, Character planter)
    {
        if (flag == null || CheckpointFlagPlanterField == null)
        {
            return;
        }

        try
        {
            CheckpointFlagPlanterField.SetValue(flag, planter);
        }
        catch
        {
            // Ignore reflection write failures.
        }
    }

    private static int GetMirageLuggageVisualState(MirageLuggage mirage)
    {
        if (mirage == null)
        {
            return 0;
        }

        Renderer[] renderers = null;
        if (MirageLuggageRenderersField != null)
        {
            try
            {
                renderers = MirageLuggageRenderersField.GetValue(mirage) as Renderer[];
            }
            catch
            {
                // Ignore reflection lookup failures.
            }
        }

        if (renderers == null || renderers.Length == 0)
        {
            return ((Component)mirage).gameObject.activeSelf ? 1 : 0;
        }

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer != null && renderer.enabled)
            {
                return 1;
            }
        }

        return 0;
    }

    private static float GetFloatFieldValue(object target, FieldInfo field, float fallback)
    {
        if (target == null || field == null)
        {
            return fallback;
        }

        try
        {
            object raw = field.GetValue(target);
            if (raw is float floatValue)
            {
                return floatValue;
            }

            if (raw is int intValue)
            {
                return intValue;
            }
        }
        catch
        {
            // Ignore reflection read failures.
        }

        return fallback;
    }

    private static bool GetBoolFieldValue(object target, FieldInfo field, bool fallback)
    {
        if (target == null || field == null)
        {
            return fallback;
        }

        try
        {
            object raw = field.GetValue(target);
            if (raw is bool boolValue)
            {
                return boolValue;
            }
        }
        catch
        {
            // Ignore reflection read failures.
        }

        return fallback;
    }

    private static void SetFloatFieldValue(object target, FieldInfo field, float value)
    {
        if (target == null || field == null)
        {
            return;
        }

        try
        {
            field.SetValue(target, value);
        }
        catch
        {
            // Ignore reflection write failures.
        }
    }

    private static void SetBoolFieldValue(object target, FieldInfo field, bool value)
    {
        if (target == null || field == null)
        {
            return;
        }

        try
        {
            field.SetValue(target, value);
        }
        catch
        {
            // Ignore reflection write failures.
        }
    }

    private static bool IsGroundRopeShooterCandidate(RopeShooter ropeShooter)
    {
        if (ropeShooter == null)
        {
            return false;
        }

        if (!((Component)ropeShooter).gameObject.activeInHierarchy)
        {
            return false;
        }

        if (ropeShooter.item != null)
        {
            return IsGroundItemCandidate(ropeShooter.item);
        }

        Transform transform = ((Component)ropeShooter).transform;
        if (transform == null)
        {
            return false;
        }

        if (transform.GetComponentInParent<GamePlayer>() != null || transform.GetComponentInParent<Character>() != null)
        {
            return false;
        }

        return transform.gameObject.scene.IsValid();
    }

    private static Campfire[] FindNonSegmentCampfires()
    {
        Campfire[] campfires = UnityObject.FindObjectsByType<Campfire>(FindObjectsSortMode.None);
        if (campfires == null || campfires.Length == 0)
        {
            return Array.Empty<Campfire>();
        }

        HashSet<Campfire> segmentCampfires = GetSegmentCampfires();
        return campfires
            .Where(campfire => campfire != null && !segmentCampfires.Contains(campfire))
            .ToArray();
    }

    private static HashSet<Campfire> GetSegmentCampfires()
    {
        HashSet<Campfire> output = new HashSet<Campfire>();
        MapHandler mapHandler = UnityObject.FindFirstObjectByType<MapHandler>();
        if (mapHandler == null || mapHandler.segments == null)
        {
            return output;
        }

        for (int i = 0; i < mapHandler.segments.Length; i++)
        {
            MapHandler.MapSegment segment = mapHandler.segments[i];
            if (segment == null || segment.segmentCampfire == null)
            {
                continue;
            }

            Campfire campfire = segment.segmentCampfire.GetComponentInChildren<Campfire>(true);
            if (campfire != null)
            {
                output.Add(campfire);
            }
        }

        return output;
    }

    private static WobbleSpinBounce[] FindBounceFungusObjects()
    {
        WobbleSpinBounce[] candidates = UnityObject.FindObjectsByType<WobbleSpinBounce>(FindObjectsSortMode.None);
        if (candidates == null || candidates.Length == 0)
        {
            return Array.Empty<WobbleSpinBounce>();
        }

        return candidates
            .Where(IsBounceFungusCandidate)
            .ToArray();
    }

    private static bool IsBounceFungusCandidate(WobbleSpinBounce component)
    {
        if (component == null || !((Component)component).gameObject.activeInHierarchy)
        {
            return false;
        }

        Transform transform = ((Component)component).transform;
        if (transform == null || !transform.gameObject.scene.IsValid())
        {
            return false;
        }

        if (transform.GetComponentInParent<GamePlayer>() != null || transform.GetComponentInParent<Character>() != null)
        {
            return false;
        }

        string objectName = NormalizeObjectName(((UnityObject)component).name);
        if (!string.IsNullOrWhiteSpace(objectName))
        {
            if (objectName.IndexOf("fung", StringComparison.OrdinalIgnoreCase) >= 0
                || objectName.IndexOf("bounce", StringComparison.OrdinalIgnoreCase) >= 0
                || objectName.IndexOf("mushroom", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        PhotonView view = ((Component)component).GetComponent<PhotonView>();
        return view != null;
    }

    private static int GetChainLauncherAmmo(RopeShooter ropeShooter)
    {
        if (ropeShooter == null)
        {
            return 0;
        }

        try
        {
            if (RopeShooterAmmoProperty != null)
            {
                object raw = RopeShooterAmmoProperty.GetValue(ropeShooter, null);
                if (raw is int ammo)
                {
                    return Mathf.Max(0, ammo);
                }
            }
        }
        catch
        {
            // Ignore reflection failures and fall back.
        }

        if (ropeShooter.item != null && ItemTotalUsesField != null)
        {
            try
            {
                object raw = ItemTotalUsesField.GetValue(ropeShooter.item);
                if (raw is int totalUses)
                {
                    return Mathf.Max(0, totalUses);
                }
            }
            catch
            {
                // Ignore fallback failures.
            }
        }

        return 0;
    }

    private static void SetChainLauncherAmmo(RopeShooter ropeShooter, int ammo)
    {
        if (ropeShooter == null)
        {
            return;
        }

        int clampedAmmo = Mathf.Max(0, ammo);
        bool applied = false;

        try
        {
            if (RopeShooterAmmoProperty != null && RopeShooterAmmoProperty.CanWrite)
            {
                RopeShooterAmmoProperty.SetValue(ropeShooter, clampedAmmo, null);
                applied = true;
            }
        }
        catch
        {
            // Ignore reflection failures and fall back.
        }

        if (!applied && ropeShooter.item != null && ItemTotalUsesField != null)
        {
            try
            {
                ItemTotalUsesField.SetValue(ropeShooter.item, clampedAmmo);
            }
            catch
            {
                // Ignore fallback failures.
            }
        }

        if (RopeShooterSyncRpcMethod != null)
        {
            try
            {
                RopeShooterSyncRpcMethod.Invoke(ropeShooter, new object[] { clampedAmmo > 0 });
            }
            catch
            {
                // Ignore RPC sync failures in single-player or on game updates.
            }
        }
    }

    private static string NormalizeSaveDisplayName(string value)
    {
        string normalized = string.IsNullOrWhiteSpace(value) ? "Save" : value.Trim();
        if (normalized.Length > 64)
        {
            normalized = normalized.Substring(0, 64).Trim();
        }

        return string.IsNullOrWhiteSpace(normalized) ? "Save" : normalized;
    }

    private string BuildUniqueSavePath(string saveName)
    {
        string baseName = SanitizeFileName(saveName);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "save";
        }

        string fullPath = Path.Combine(saveDirectory, $"{baseName}.peaksave.json");
        int suffix = 2;
        while (File.Exists(fullPath) && suffix < 10000)
        {
            fullPath = Path.Combine(saveDirectory, $"{baseName}_{suffix}.peaksave.json");
            suffix++;
        }

        return fullPath;
    }

    private string ResolveSavePathForName(string saveName)
    {
        string normalized = NormalizeSaveDisplayName(saveName);
        if (normalized.Equals("Autosave", StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(saveDirectory, "Autosave.peaksave.json");
        }

        return BuildUniqueSavePath(normalized);
    }

    private void CleanupLegacyAutosaves(string activeAutosavePath)
    {
        try
        {
            if (!EnsureSaveDirectoryReady(showStatus: false))
            {
                return;
            }

            string normalizedActivePath = string.IsNullOrWhiteSpace(activeAutosavePath)
                ? string.Empty
                : Path.GetFullPath(activeAutosavePath);
            FileInfo[] legacyAutosaves = new DirectoryInfo(saveDirectory).GetFiles("Autosave_*.peaksave.json", SearchOption.TopDirectoryOnly);
            for (int i = 0; i < legacyAutosaves.Length; i++)
            {
                FileInfo legacyFile = legacyAutosaves[i];
                if (legacyFile == null)
                {
                    continue;
                }

                string candidatePath = Path.GetFullPath(legacyFile.FullName);
                if (!string.IsNullOrWhiteSpace(normalizedActivePath)
                    && string.Equals(candidatePath, normalizedActivePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    legacyFile.Delete();
                }
                catch
                {
                    // Ignore cleanup failure for individual legacy autosave files.
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Failed to clean legacy autosaves: {ex.Message}");
        }
    }

    private bool EnsureSaveDirectoryReady(bool showStatus)
    {
        string[] candidates =
        {
            saveDirectory,
            preferredSaveDirectory,
            fallbackSaveDirectory
        };

        string lastError = string.Empty;
        foreach (string candidate in candidates.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (TryActivateSaveDirectory(candidate, out string reason))
            {
                return true;
            }

            lastError = reason;
            Logger.LogWarning($"Save directory '{candidate}' is unavailable: {reason}");
        }

        if (showStatus)
        {
            SetStatus(string.IsNullOrWhiteSpace(lastError) ? "Save failed: no writable save folder." : $"Save failed: {lastError}", Color.red, 6f);
        }

        return false;
    }

    private bool TryActivateSaveDirectory(string candidate, out string failureReason)
    {
        failureReason = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            failureReason = "invalid save folder path";
            return false;
        }

        try
        {
            Directory.CreateDirectory(candidate);
            string probePath = Path.Combine(candidate, ".taynsm_write_test.tmp");
            File.WriteAllText(probePath, "ok");
            File.Delete(probePath);

            if (!string.Equals(saveDirectory, candidate, StringComparison.OrdinalIgnoreCase))
            {
                Logger.LogWarning($"Switching save directory to '{candidate}'.");
            }

            saveDirectory = candidate;
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            failureReason = "save folder is not writable";
            return false;
        }
        catch (Exception ex)
        {
            failureReason = string.IsNullOrWhiteSpace(ex.Message) ? "save folder is unavailable" : ex.Message;
            return false;
        }
    }

    private static string BuildSaveFailureStatus(Exception ex)
    {
        if (ex is UnauthorizedAccessException)
        {
            return "Save failed: save folder is not writable.";
        }

        string message = ex?.GetBaseException()?.Message;
        if (string.IsNullOrWhiteSpace(message))
        {
            return "Save failed. Check BepInEx log.";
        }

        message = message.Trim();
        int newlineIndex = message.IndexOfAny(new[] { '\r', '\n' });
        if (newlineIndex >= 0)
        {
            message = message.Substring(0, newlineIndex).Trim();
        }

        if (message.Length > 90)
        {
            message = message.Substring(0, 90).TrimEnd() + "...";
        }

        return $"Save failed: {message}";
    }

    private static string BuildLoadFailureStatus(Exception ex)
    {
        string message = ex?.GetBaseException()?.Message;
        if (string.IsNullOrWhiteSpace(message))
        {
            return "Load failed. Check BepInEx log.";
        }

        message = message.Trim();
        int newlineIndex = message.IndexOfAny(new[] { '\r', '\n' });
        if (newlineIndex >= 0)
        {
            message = message.Substring(0, newlineIndex).Trim();
        }

        if (message.Length > 90)
        {
            message = message.Substring(0, 90).TrimEnd() + "...";
        }

        return $"Load failed: {message}";
    }

    private static string Safe(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "-" : value;
    }

    internal static string FormatRunSummary(SaveMetadata metadata)
    {
        if (metadata == null)
        {
            return "Run: Day -  Time: -  Duration: -  Difficulty: -";
        }

        string dayText = metadata.runDay.HasValue
            ? "Day " + metadata.runDay.Value.ToString(CultureInfo.InvariantCulture)
            : "Day -";
        string timeText = ResolveInGameTimeText(metadata);
        string durationText = FormatRunDuration(metadata.runTimeSeconds);
        string difficultyText = FormatDifficultyLabel(metadata.ascent);
        return $"Run: {dayText}  Time: {timeText}  Duration: {durationText}  Difficulty: {difficultyText}";
    }

    internal static string FormatDifficultyLabel(int ascent)
    {
        if (ascent < 0)
        {
            return "Tenderfoot";
        }

        if (ascent == 0)
        {
            return "Peak";
        }

        return "Ascent " + ascent.ToString(CultureInfo.InvariantCulture);
    }

    private static string ResolveInGameTimeText(SaveMetadata metadata)
    {
        if (metadata == null)
        {
            return "-";
        }

        if (!string.IsNullOrWhiteSpace(metadata.inGameTime))
        {
            return metadata.inGameTime.Trim();
        }

        if (!metadata.timeOfDay.HasValue)
        {
            return "-";
        }

        float source = metadata.timeOfDay.Value;
        if (float.IsNaN(source) || float.IsInfinity(source))
        {
            return "-";
        }

        float hours = source;
        if (hours >= 0f && hours <= 1.01f)
        {
            hours *= 24f;
        }

        hours = Mathf.Repeat(hours, 24f);
        int hour = Mathf.FloorToInt(hours);
        int minutes = Mathf.RoundToInt((hours - hour) * 60f);
        if (minutes >= 60)
        {
            minutes -= 60;
            hour = (hour + 1) % 24;
        }

        int hour12 = hour % 12;
        if (hour12 == 0)
        {
            hour12 = 12;
        }

        string amPm = hour >= 12 ? "PM" : "AM";
        return hour12.ToString(CultureInfo.InvariantCulture) + ":" + minutes.ToString("00", CultureInfo.InvariantCulture) + " " + amPm;
    }

    private static string FormatRunDuration(float? runTimeSeconds)
    {
        if (!runTimeSeconds.HasValue)
        {
            return "-";
        }

        float raw = runTimeSeconds.Value;
        if (float.IsNaN(raw) || float.IsInfinity(raw))
        {
            return "-";
        }

        TimeSpan duration = TimeSpan.FromSeconds(Math.Max(0d, raw));
        if (duration.TotalHours >= 1d)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:D2}:{1:D2}:{2:D2}",
                (int)duration.TotalHours,
                duration.Minutes,
                duration.Seconds
            );
        }

        return string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}", duration.Minutes, duration.Seconds);
    }

    private static string FormatBytes(long size)
    {
        if (size < 1024)
        {
            return $"{size} B";
        }

        if (size < 1024 * 1024)
        {
            return $"{size / 1024f:F1} KB";
        }

        return $"{size / (1024f * 1024f):F1} MB";
    }

    private void SetStatus(string message, Color color, float durationSeconds)
    {
        statusMessage = message;
        statusColor = color;
        statusMessageUntil = Time.unscaledTime + Mathf.Max(0.5f, durationSeconds);
    }

    private static Texture2D MakeTexture(Color color)
    {
        Texture2D texture = new Texture2D(2, 2)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        Color[] pixels = { color, color, color, color };
        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }

    private static Texture2D MakeRoundedTexture(Color color, int cornerRadius, int size = 48)
    {
        int width = Mathf.Max(8, size);
        int height = Mathf.Max(8, size);
        int radius = Mathf.Clamp(cornerRadius, 0, Mathf.Min(width, height) / 2);
        float radiusSquared = radius * radius;

        Texture2D texture = new Texture2D(width, height, TextureFormat.ARGB32, mipChain: false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        Color clear = new Color(0f, 0f, 0f, 0f);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool inside = true;

                if (x < radius && y < radius)
                {
                    float dx = radius - x - 0.5f;
                    float dy = radius - y - 0.5f;
                    inside = dx * dx + dy * dy <= radiusSquared;
                }
                else if (x >= width - radius && y < radius)
                {
                    float dx = x - (width - radius) + 0.5f;
                    float dy = radius - y - 0.5f;
                    inside = dx * dx + dy * dy <= radiusSquared;
                }
                else if (x < radius && y >= height - radius)
                {
                    float dx = radius - x - 0.5f;
                    float dy = y - (height - radius) + 0.5f;
                    inside = dx * dx + dy * dy <= radiusSquared;
                }
                else if (x >= width - radius && y >= height - radius)
                {
                    float dx = x - (width - radius) + 0.5f;
                    float dy = y - (height - radius) + 0.5f;
                    inside = dx * dx + dy * dy <= radiusSquared;
                }

                texture.SetPixel(x, y, inside ? color : clear);
            }
        }

        texture.Apply();
        return texture;
    }

    private void EnsureStyles()
    {
        if (stylesBuilt)
        {
            return;
        }

        overlayTexture = MakeTexture(new Color(0.01f, 0.04f, 0.08f, 0.26f));
        windowTexture = MakeTexture(new Color(0.08f, 0.13f, 0.18f, 0.98f));
        sectionTexture = MakeTexture(new Color(0.12f, 0.18f, 0.24f, 0.98f));
        cardTexture = MakeTexture(new Color(0.10f, 0.16f, 0.22f, 0.96f));
        warningCardTexture = MakeTexture(new Color(0.28f, 0.21f, 0.07f, 0.96f));
        buttonTexture = MakeRoundedTexture(new Color(0.16f, 0.43f, 0.35f, 0.98f), 10);
        buttonHoverTexture = MakeRoundedTexture(new Color(0.19f, 0.54f, 0.43f, 0.98f), 10);
        dangerButtonTexture = MakeRoundedTexture(new Color(0.58f, 0.18f, 0.18f, 0.98f), 10);
        textFieldTexture = MakeRoundedTexture(new Color(0.08f, 0.11f, 0.14f, 1f), 8);

        windowStyle = new GUIStyle(GUI.skin.window)
        {
            normal = { background = windowTexture, textColor = Color.white },
            fontStyle = FontStyle.Bold,
            fontSize = 14,
            padding = new RectOffset(12, 12, 24, 12)
        };

        sectionStyle = new GUIStyle(GUI.skin.box)
        {
            normal = { background = sectionTexture, textColor = Color.white },
            border = new RectOffset(6, 6, 6, 6),
            padding = new RectOffset(10, 10, 10, 10),
            margin = new RectOffset(3, 3, 3, 3)
        };

        titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };

        subtitleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.78f, 0.92f, 1f, 1f) }
        };

        normalLabelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            wordWrap = true,
            normal = { textColor = new Color(0.88f, 0.92f, 0.95f, 1f) }
        };

        errorLabelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            wordWrap = true,
            normal = { textColor = new Color(1f, 0.88f, 0.35f, 1f) }
        };

        softButtonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 11,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { background = buttonTexture, textColor = Color.white },
            hover = { background = buttonHoverTexture, textColor = Color.white },
            active = { background = buttonHoverTexture, textColor = Color.white },
            border = new RectOffset(10, 10, 10, 10),
            padding = new RectOffset(8, 8, 5, 5)
        };

        dangerButtonStyle = new GUIStyle(softButtonStyle)
        {
            normal = { background = dangerButtonTexture, textColor = Color.white },
            hover = { background = dangerButtonTexture, textColor = Color.white },
            active = { background = dangerButtonTexture, textColor = Color.white }
        };

        textFieldStyle = new GUIStyle(GUI.skin.textField)
        {
            normal = { background = textFieldTexture, textColor = Color.white },
            focused = { background = textFieldTexture, textColor = Color.white },
            border = new RectOffset(8, 8, 8, 8),
            padding = new RectOffset(8, 8, 5, 5)
        };

        cardStyle = new GUIStyle(GUI.skin.box)
        {
            normal = { background = cardTexture, textColor = Color.white },
            border = new RectOffset(4, 4, 4, 4),
            padding = new RectOffset(8, 8, 8, 8),
            margin = new RectOffset(1, 1, 1, 1)
        };

        cardWarningStyle = new GUIStyle(cardStyle)
        {
            normal = { background = warningCardTexture, textColor = Color.white }
        };

        stylesBuilt = true;
    }

    private void DrawOverlay()
    {
        Color previous = GUI.color;
        GUI.color = Color.white;
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), overlayTexture, ScaleMode.StretchToFill);
        GUI.color = previous;
    }

    private static void SetPendingSeedForLoad(int seed)
    {
        PendingSeedForLoad = seed;
        PendingSeedSetRealtime = Time.realtimeSinceStartup;
    }

    private static void ClearPendingSeedForLoad()
    {
        PendingSeedForLoad = null;
        PendingSeedSetRealtime = 0f;
    }

    private void DestroyUiResources()
    {
        if (overlayTexture != null) Destroy(overlayTexture);
        if (windowTexture != null) Destroy(windowTexture);
        if (sectionTexture != null) Destroy(sectionTexture);
        if (cardTexture != null) Destroy(cardTexture);
        if (warningCardTexture != null) Destroy(warningCardTexture);
        if (buttonTexture != null) Destroy(buttonTexture);
        if (buttonHoverTexture != null) Destroy(buttonHoverTexture);
        if (dangerButtonTexture != null) Destroy(dangerButtonTexture);
        if (textFieldTexture != null) Destroy(textFieldTexture);
    }

    internal static bool TryGetPendingSeed(out int seed)
    {
        if (!PendingSeedForLoad.HasValue)
        {
            seed = 0;
            return false;
        }

        if (Instance == null || !Instance.isLoading)
        {
            ClearPendingSeedForLoad();
            seed = 0;
            return false;
        }

        if (PendingSeedSetRealtime > 0f && Time.realtimeSinceStartup - PendingSeedSetRealtime > PendingSeedLifetimeSeconds)
        {
            Instance.Logger.LogWarning("Discarded stale pending seed override.");
            ClearPendingSeedForLoad();
            seed = 0;
            return false;
        }

        seed = PendingSeedForLoad.Value;
        return true;
    }
}

internal sealed class SaveManagerPausePage : UIPage
{
    private SaveManagerPlugin plugin;

    private UIPageHandler handler;

    private UIPage returnPage;

    private Button templateButton;

    private TMP_Text templateButtonText;

    private bool uiBuilt;

    private RectTransform rootContainer;

    private TextMeshProUGUI statusLabel;

    private RectTransform quitDecisionRow;

    private RectTransform standardSaveRow;

    private RectTransform saveListContent;

    private GameObject slotActionPanel;

    private TextMeshProUGUI slotActionLabel;

    private Button loadSelectedButton;

    private Button overwriteSelectedButton;

    private Button deleteSelectedButton;

    private GameObject confirmPanel;

    private TextMeshProUGUI confirmLabel;

    private Action confirmAction;

    private GameObject nameEntryPanel;

    private TMP_InputField nameEntryInput;

    private TextMeshProUGUI nameEntryTitle;

    private Action<string> nameEntryConfirmAction;

    private float nextStatusRefresh;

    private MonoBehaviour templateClickSfxComponent;

    private MethodInfo templateClickSfxPlayMethod;

    private MonoBehaviour templateHoverFeedbackComponent;

    private MethodInfo templateHoverOnClickMethod;

    private readonly Dictionary<RectTransform, Coroutine> activePressAnimations = new Dictionary<RectTransform, Coroutine>();

    private readonly List<SaveListEntry> displayedEntries = new List<SaveListEntry>();

    private readonly List<Image> displayedSlotBackgrounds = new List<Image>();

    private readonly List<Outline> displayedSlotOutlines = new List<Outline>();

    private readonly List<TextMeshProUGUI> displayedSlotTitles = new List<TextMeshProUGUI>();

    private int selectedSaveIndex = -1;

    private enum ButtonVisualStyle
    {
        Primary,
        Secondary,
        Neutral,
        Danger
    }

    public void Initialize(SaveManagerPlugin plugin, UIPageHandler handler, UIPage returnPage, Button templateButton)
    {
        this.plugin = plugin;
        this.handler = handler;
        this.returnPage = returnPage;
        this.templateButton = templateButton;
        this.templateButtonText = templateButton != null ? templateButton.GetComponentInChildren<TMP_Text>(true) : null;
        CacheTemplateFeedbackComponents();

        if (!uiBuilt)
        {
            BuildUi();
        }
    }

    public override void OnPageEnter()
    {
        base.OnPageEnter();
        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }

        RefreshFromSource();
    }

    public override void OnPageExit()
    {
        base.OnPageExit();
        StopAllPressAnimations();
        HideSlotActionPanel();
        HideConfirmation();
        HideNameEntryPanel();
    }

    public void PrepareForLoad()
    {
        StopAllPressAnimations();
        HideSlotActionPanel();
        HideConfirmation();
        HideNameEntryPanel();
        if (gameObject.activeSelf)
        {
            gameObject.SetActive(false);
        }
    }

    private void Update()
    {
        if (!gameObject.activeInHierarchy)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (nameEntryPanel != null && nameEntryPanel.activeSelf)
            {
                HideNameEntryPanel();
            }
            else if (confirmPanel != null && confirmPanel.activeSelf)
            {
                HideConfirmation();
            }
            else if (slotActionPanel != null && slotActionPanel.activeSelf)
            {
                HideSlotActionPanel();
            }
            else
            {
                GoBack();
            }

            return;
        }

        if (Time.unscaledTime < nextStatusRefresh)
        {
            return;
        }

        UpdateStatusLabel();
        UpdateSlotActionPanelState();
        nextStatusRefresh = Time.unscaledTime + 0.25f;
    }

    public void RefreshFromSource()
    {
        if (plugin == null)
        {
            return;
        }

        string preferredPath = GetSelectedEntry()?.fullPath;
        plugin.UiRefreshSaveList();
        RebuildSaveRows(preferredPath);
        UpdateStatusLabel();
        UpdateQuitDecisionRow();
        HideSlotActionPanel();
        HideConfirmation();
        HideNameEntryPanel();
    }

    private void BuildUi()
    {
        uiBuilt = true;

        RectTransform pageRect = transform as RectTransform;
        if (pageRect == null)
        {
            return;
        }

        rootContainer = new GameObject("SaveManagerRoot", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter)).GetComponent<RectTransform>();
        rootContainer.SetParent(pageRect, worldPositionStays: false);
        rootContainer.anchorMin = new Vector2(0.09f, 0.07f);
        rootContainer.anchorMax = new Vector2(0.91f, 0.93f);
        rootContainer.offsetMin = Vector2.zero;
        rootContainer.offsetMax = Vector2.zero;

        Image rootImage = rootContainer.GetComponent<Image>();
        rootImage.color = new Color(0f, 0f, 0f, 0.22f);

        VerticalLayoutGroup rootLayout = rootContainer.GetComponent<VerticalLayoutGroup>();
        rootLayout.spacing = 8f;
        rootLayout.padding = new RectOffset(16, 16, 14, 14);
        rootLayout.childControlHeight = true;
        rootLayout.childControlWidth = true;
        rootLayout.childForceExpandHeight = false;
        rootLayout.childForceExpandWidth = true;

        ContentSizeFitter rootFitter = rootContainer.GetComponent<ContentSizeFitter>();
        rootFitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
        rootFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        RectTransform titleRow = CreateRow(rootContainer, 44f);
        CreateButton(titleRow, "BACK", GoBack, 108f, ButtonVisualStyle.Danger);
        TextMeshProUGUI titleLabel = CreateLabel(titleRow, "SAVE MANAGER", 36f, TextAlignmentOptions.Left, 28);
        titleLabel.fontStyle = FontStyles.UpperCase;

        standardSaveRow = CreateRow(rootContainer, 40f);
        CreateButton(standardSaveRow, "QUICK SAVE", QuickSaveClicked, 150f, ButtonVisualStyle.Secondary);
        CreateButton(standardSaveRow, "SAVE", SaveClicked, 120f, ButtonVisualStyle.Primary);
        CreateButton(standardSaveRow, "REFRESH", RefreshFromSource, 114f, ButtonVisualStyle.Neutral);

        statusLabel = CreateLabel(rootContainer, string.Empty, 26f, TextAlignmentOptions.Left, 16);
        statusLabel.color = templateButtonText != null ? templateButtonText.color : new Color(0.9f, 0.9f, 0.9f, 0.95f);

        quitDecisionRow = new GameObject("QuitDecisionRow", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement)).GetComponent<RectTransform>();
        quitDecisionRow.SetParent(rootContainer, worldPositionStays: false);
        VerticalLayoutGroup quitLayout = quitDecisionRow.GetComponent<VerticalLayoutGroup>();
        quitLayout.spacing = 6f;
        quitLayout.padding = new RectOffset(0, 0, 0, 0);
        quitLayout.childControlHeight = true;
        quitLayout.childControlWidth = true;
        quitLayout.childForceExpandHeight = false;
        quitLayout.childForceExpandWidth = true;
        LayoutElement quitElement = quitDecisionRow.GetComponent<LayoutElement>();
        quitElement.minHeight = 44f;
        quitElement.preferredHeight = 44f;

        RectTransform quitRow = CreateRow(quitDecisionRow, 42f);
        CreateButton(quitRow, "SAVE & QUIT", SaveAndQuitClicked, 176f, ButtonVisualStyle.Secondary);
        CreateButton(quitRow, "CANCEL QUIT", CancelQuitClicked, 156f, ButtonVisualStyle.Neutral);
        quitDecisionRow.gameObject.SetActive(false);

        RectTransform listContainer = new GameObject("SaveListContainer", typeof(RectTransform), typeof(Image), typeof(LayoutElement)).GetComponent<RectTransform>();
        listContainer.SetParent(rootContainer, worldPositionStays: false);
        Image listBackground = listContainer.GetComponent<Image>();
        listBackground.color = new Color(0f, 0f, 0f, 0.35f);

        LayoutElement listLayout = listContainer.GetComponent<LayoutElement>();
        listLayout.preferredHeight = 488f;
        listLayout.minHeight = 360f;
        listLayout.flexibleHeight = 1f;

        ScrollRect scrollRect = listContainer.gameObject.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 32f;

        RectTransform viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask)).GetComponent<RectTransform>();
        viewport.SetParent(listContainer, worldPositionStays: false);
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = new Vector2(10f, 10f);
        viewport.offsetMax = new Vector2(-28f, -10f);
        viewport.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.12f);
        viewport.GetComponent<Mask>().showMaskGraphic = false;

        saveListContent = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter)).GetComponent<RectTransform>();
        saveListContent.SetParent(viewport, worldPositionStays: false);
        saveListContent.anchorMin = new Vector2(0f, 1f);
        saveListContent.anchorMax = new Vector2(1f, 1f);
        saveListContent.pivot = new Vector2(0.5f, 1f);
        saveListContent.offsetMin = new Vector2(2f, 0f);
        saveListContent.offsetMax = new Vector2(-2f, 0f);

        VerticalLayoutGroup contentLayout = saveListContent.GetComponent<VerticalLayoutGroup>();
        contentLayout.spacing = 8f;
        contentLayout.padding = new RectOffset(2, 2, 2, 2);
        contentLayout.childControlHeight = true;
        contentLayout.childControlWidth = true;
        contentLayout.childForceExpandHeight = false;
        contentLayout.childForceExpandWidth = true;

        ContentSizeFitter contentFitter = saveListContent.GetComponent<ContentSizeFitter>();
        contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        contentFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        Scrollbar verticalScrollbar = CreateVerticalScrollbar(listContainer);
        scrollRect.viewport = viewport;
        scrollRect.content = saveListContent;
        scrollRect.verticalScrollbar = verticalScrollbar;
        scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

        BuildSlotActionPanel();
        BuildNameEntryPanel();
        BuildConfirmationPanel();
    }

    private void BuildSlotActionPanel()
    {
        slotActionPanel = new GameObject("SlotActionOverlay", typeof(RectTransform), typeof(Image), typeof(LayoutElement)).gameObject;
        RectTransform overlayRect = slotActionPanel.GetComponent<RectTransform>();
        overlayRect.SetParent(rootContainer, worldPositionStays: false);
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;
        LayoutElement overlayLayout = slotActionPanel.GetComponent<LayoutElement>();
        overlayLayout.ignoreLayout = true;

        Image overlayImage = slotActionPanel.GetComponent<Image>();
        overlayImage.color = new Color(0f, 0f, 0f, 0.62f);
        overlayImage.raycastTarget = true;

        RectTransform panelRect = new GameObject("SlotActionPanel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup)).GetComponent<RectTransform>();
        panelRect.SetParent(overlayRect, worldPositionStays: false);
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(700f, 250f);

        Image panelImage = panelRect.GetComponent<Image>();
        panelImage.color = new Color(0f, 0f, 0f, 0.88f);

        VerticalLayoutGroup layout = panelRect.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 10f;
        layout.padding = new RectOffset(18, 18, 16, 16);
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        CreateLabel(panelRect, "SAVE ACTIONS", 30f, TextAlignmentOptions.Center, 24);
        slotActionLabel = CreateLabel(panelRect, "Select an action.", 84f, TextAlignmentOptions.Center, 18);
        slotActionLabel.textWrappingMode = TextWrappingModes.Normal;
        slotActionLabel.overflowMode = TextOverflowModes.Overflow;

        RectTransform actionRow = CreateRow(panelRect, 44f);
        HorizontalLayoutGroup actionLayout = actionRow.GetComponent<HorizontalLayoutGroup>();
        if (actionLayout != null)
        {
            actionLayout.childAlignment = TextAnchor.MiddleCenter;
            actionLayout.spacing = 12f;
        }

        loadSelectedButton = CreateButton(actionRow, "LOAD", LoadSelectedClicked, 168f, ButtonVisualStyle.Primary);
        overwriteSelectedButton = CreateButton(actionRow, "OVERWRITE", OverwriteSelectedClicked, 180f, ButtonVisualStyle.Secondary);
        deleteSelectedButton = CreateButton(actionRow, "DELETE", DeleteSelectedClicked, 138f, ButtonVisualStyle.Danger);

        RectTransform cancelRow = CreateRow(panelRect, 40f);
        HorizontalLayoutGroup cancelLayout = cancelRow.GetComponent<HorizontalLayoutGroup>();
        if (cancelLayout != null)
        {
            cancelLayout.childAlignment = TextAnchor.MiddleCenter;
            cancelLayout.spacing = 0f;
        }

        CreateButton(cancelRow, "CANCEL", HideSlotActionPanel, 160f, ButtonVisualStyle.Neutral);
        slotActionPanel.SetActive(false);
    }

    private void BuildConfirmationPanel()
    {
        confirmPanel = new GameObject("ConfirmOverlay", typeof(RectTransform), typeof(Image), typeof(LayoutElement)).gameObject;
        RectTransform overlayRect = confirmPanel.GetComponent<RectTransform>();
        overlayRect.SetParent(rootContainer, worldPositionStays: false);
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;
        LayoutElement overlayLayout = confirmPanel.GetComponent<LayoutElement>();
        overlayLayout.ignoreLayout = true;

        Image overlayImage = confirmPanel.GetComponent<Image>();
        overlayImage.color = new Color(0f, 0f, 0f, 0.62f);
        overlayImage.raycastTarget = true;

        RectTransform confirmRect = new GameObject("ConfirmPanel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup)).GetComponent<RectTransform>();
        confirmRect.SetParent(overlayRect, worldPositionStays: false);
        confirmRect.anchorMin = new Vector2(0.5f, 0.5f);
        confirmRect.anchorMax = new Vector2(0.5f, 0.5f);
        confirmRect.pivot = new Vector2(0.5f, 0.5f);
        confirmRect.sizeDelta = new Vector2(620f, 250f);

        Image panelImage = confirmRect.GetComponent<Image>();
        panelImage.color = new Color(0f, 0f, 0f, 0.88f);

        VerticalLayoutGroup layout = confirmRect.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 10f;
        layout.padding = new RectOffset(18, 18, 16, 16);
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        CreateLabel(confirmRect, "ARE YOU SURE?", 28f, TextAlignmentOptions.Center, 24);
        confirmLabel = CreateLabel(confirmRect, string.Empty, 92f, TextAlignmentOptions.Center, 18);
        confirmLabel.textWrappingMode = TextWrappingModes.Normal;
        confirmLabel.overflowMode = TextOverflowModes.Overflow;

        RectTransform actionRow = CreateRow(confirmRect, 42f);
        HorizontalLayoutGroup confirmActionsLayout = actionRow.GetComponent<HorizontalLayoutGroup>();
        if (confirmActionsLayout != null)
        {
            confirmActionsLayout.childAlignment = TextAnchor.MiddleCenter;
            confirmActionsLayout.spacing = 12f;
        }

        CreateButton(actionRow, "CANCEL", HideConfirmation, 144f, ButtonVisualStyle.Neutral);
        CreateButton(actionRow, "CONFIRM", ConfirmActionClicked, 148f, ButtonVisualStyle.Primary);

        confirmPanel.SetActive(false);
    }

    private void BuildNameEntryPanel()
    {
        nameEntryPanel = new GameObject("SaveNameOverlay", typeof(RectTransform), typeof(Image), typeof(LayoutElement)).gameObject;
        RectTransform overlayRect = nameEntryPanel.GetComponent<RectTransform>();
        overlayRect.SetParent(rootContainer, worldPositionStays: false);
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;
        LayoutElement overlayLayout = nameEntryPanel.GetComponent<LayoutElement>();
        overlayLayout.ignoreLayout = true;

        Image overlayImage = nameEntryPanel.GetComponent<Image>();
        overlayImage.color = new Color(0f, 0f, 0f, 0.62f);
        overlayImage.raycastTarget = true;

        RectTransform panelRect = new GameObject("SaveNamePanel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup)).GetComponent<RectTransform>();
        panelRect.SetParent(overlayRect, worldPositionStays: false);
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(620f, 250f);

        Image panelImage = panelRect.GetComponent<Image>();
        panelImage.color = new Color(0f, 0f, 0f, 0.88f);

        VerticalLayoutGroup layout = panelRect.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 10f;
        layout.padding = new RectOffset(18, 18, 16, 16);
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        nameEntryTitle = CreateLabel(panelRect, "NAME SAVE", 32f, TextAlignmentOptions.Center, 24);
        nameEntryInput = CreateInput(panelRect);

        RectTransform actionRow = CreateRow(panelRect, 42f);
        HorizontalLayoutGroup nameActionsLayout = actionRow.GetComponent<HorizontalLayoutGroup>();
        if (nameActionsLayout != null)
        {
            nameActionsLayout.childAlignment = TextAnchor.MiddleCenter;
            nameActionsLayout.spacing = 12f;
        }

        CreateButton(actionRow, "CANCEL", HideNameEntryPanel, 146f, ButtonVisualStyle.Neutral);
        CreateButton(actionRow, "CONFIRM", ConfirmNameEntry, 146f, ButtonVisualStyle.Primary);

        nameEntryPanel.SetActive(false);
    }

    private void CacheTemplateFeedbackComponents()
    {
        templateClickSfxComponent = null;
        templateClickSfxPlayMethod = null;
        templateHoverFeedbackComponent = null;
        templateHoverOnClickMethod = null;

        if (templateButton == null)
        {
            return;
        }

        MonoBehaviour[] behaviours = templateButton.GetComponentsInChildren<MonoBehaviour>(includeInactive: true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null)
            {
                continue;
            }

            Type type = behaviour.GetType();
            if (templateClickSfxComponent == null && string.Equals(type.Name, "SFX_PlayOneShot", StringComparison.Ordinal))
            {
                MethodInfo playMethod = type.GetMethod("Play", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                if (playMethod != null)
                {
                    templateClickSfxComponent = behaviour;
                    templateClickSfxPlayMethod = playMethod;
                }
            }

            if (templateHoverFeedbackComponent == null && string.Equals(type.Name, "ButtonHoverFeedback", StringComparison.Ordinal))
            {
                MethodInfo clickMethod = type.GetMethod("OnClick", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                if (clickMethod != null)
                {
                    templateHoverFeedbackComponent = behaviour;
                    templateHoverOnClickMethod = clickMethod;
                }
            }
        }
    }

    private void PlayButtonFeedback(RectTransform rectTransform)
    {
        bool playedLocalHooks = rectTransform != null && TriggerObjectClickHooks(rectTransform.gameObject);
        if (!playedLocalHooks)
        {
            TriggerTemplateClickHooks();
        }

        if (rectTransform == null)
        {
            return;
        }

        if (activePressAnimations.TryGetValue(rectTransform, out Coroutine existing) && existing != null)
        {
            StopCoroutine(existing);
            activePressAnimations.Remove(rectTransform);
        }

        Coroutine routine = StartCoroutine(AnimateButtonPress(rectTransform));
        activePressAnimations[rectTransform] = routine;
    }

    private bool TriggerObjectClickHooks(GameObject gameObject)
    {
        if (gameObject == null)
        {
            return false;
        }

        bool invokedAny = false;
        MonoBehaviour[] behaviours = gameObject.GetComponentsInChildren<MonoBehaviour>(includeInactive: true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null)
            {
                continue;
            }

            Type type = behaviour.GetType();
            if (string.Equals(type.Name, "ButtonHoverFeedback", StringComparison.Ordinal))
            {
                MethodInfo clickMethod = type.GetMethod("OnClick", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                if (TryInvokeClickMethod(behaviour, clickMethod))
                {
                    invokedAny = true;
                }
            }
            else if (string.Equals(type.Name, "SFX_PlayOneShot", StringComparison.Ordinal))
            {
                MethodInfo playMethod = type.GetMethod("Play", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                if (TryInvokeClickMethod(behaviour, playMethod))
                {
                    invokedAny = true;
                }
            }
        }

        return invokedAny;
    }

    private bool TriggerTemplateClickHooks()
    {
        bool invokedAny = false;
        if (TryInvokeClickMethod(templateHoverFeedbackComponent, templateHoverOnClickMethod))
        {
            invokedAny = true;
        }

        if (TryInvokeClickMethod(templateClickSfxComponent, templateClickSfxPlayMethod))
        {
            invokedAny = true;
        }

        return invokedAny;
    }

    private static bool TryInvokeClickMethod(MonoBehaviour behaviour, MethodInfo method)
    {
        if (behaviour == null || method == null)
        {
            return false;
        }

        try
        {
            method.Invoke(behaviour, null);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private IEnumerator AnimateButtonPress(RectTransform rectTransform)
    {
        if (rectTransform == null)
        {
            yield break;
        }

        Vector3 originalScale = rectTransform.localScale;
        Vector3 pressedScale = new Vector3(originalScale.x * 0.965f, originalScale.y * 0.965f, originalScale.z);
        const float pressDuration = 0.045f;
        const float releaseDuration = 0.08f;

        float elapsed = 0f;
        while (elapsed < pressDuration && rectTransform != null)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / pressDuration);
            rectTransform.localScale = Vector3.LerpUnclamped(originalScale, pressedScale, t);
            yield return null;
        }

        elapsed = 0f;
        while (elapsed < releaseDuration && rectTransform != null)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / releaseDuration);
            rectTransform.localScale = Vector3.LerpUnclamped(pressedScale, originalScale, t);
            yield return null;
        }

        if (rectTransform != null)
        {
            rectTransform.localScale = originalScale;
            activePressAnimations.Remove(rectTransform);
        }
    }

    private void StopAllPressAnimations()
    {
        foreach (KeyValuePair<RectTransform, Coroutine> pair in activePressAnimations)
        {
            if (pair.Value != null)
            {
                StopCoroutine(pair.Value);
            }

            if (pair.Key != null)
            {
                pair.Key.localScale = Vector3.one;
            }
        }

        activePressAnimations.Clear();
    }

    private Scrollbar CreateVerticalScrollbar(Transform parent)
    {
        RectTransform scrollBarRect = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar)).GetComponent<RectTransform>();
        scrollBarRect.SetParent(parent, worldPositionStays: false);
        scrollBarRect.anchorMin = new Vector2(1f, 0f);
        scrollBarRect.anchorMax = new Vector2(1f, 1f);
        scrollBarRect.pivot = new Vector2(1f, 0.5f);
        scrollBarRect.offsetMin = new Vector2(-18f, 10f);
        scrollBarRect.offsetMax = new Vector2(-6f, -10f);

        Image background = scrollBarRect.GetComponent<Image>();
        background.color = new Color(0f, 0f, 0f, 0.32f);

        RectTransform slidingArea = new GameObject("Sliding Area", typeof(RectTransform)).GetComponent<RectTransform>();
        slidingArea.SetParent(scrollBarRect, worldPositionStays: false);
        slidingArea.anchorMin = Vector2.zero;
        slidingArea.anchorMax = Vector2.one;
        slidingArea.offsetMin = Vector2.zero;
        slidingArea.offsetMax = Vector2.zero;

        RectTransform handle = new GameObject("Handle", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        handle.SetParent(slidingArea, worldPositionStays: false);
        handle.anchorMin = Vector2.zero;
        handle.anchorMax = Vector2.one;
        handle.offsetMin = new Vector2(1f, 1f);
        handle.offsetMax = new Vector2(-1f, -1f);

        Image handleImage = handle.GetComponent<Image>();
        handleImage.color = new Color(0.92f, 0.92f, 0.92f, 0.95f);

        Scrollbar scrollbar = scrollBarRect.GetComponent<Scrollbar>();
        scrollbar.targetGraphic = handleImage;
        scrollbar.handleRect = handle;
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scrollbar.size = 0.28f;
        return scrollbar;
    }

    private RectTransform CreateRow(Transform parent, float minHeight)
    {
        RectTransform row = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement)).GetComponent<RectTransform>();
        row.SetParent(parent, worldPositionStays: false);

        HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.padding = new RectOffset(0, 0, 0, 0);
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = false;

        LayoutElement element = row.GetComponent<LayoutElement>();
        element.minHeight = minHeight;
        element.preferredHeight = minHeight;
        return row;
    }

    private TextMeshProUGUI CreateLabel(Transform parent, string text, float minHeight, TextAlignmentOptions alignment, int fontSize)
    {
        RectTransform labelRect = new GameObject("Label", typeof(RectTransform), typeof(LayoutElement)).GetComponent<RectTransform>();
        labelRect.SetParent(parent, worldPositionStays: false);

        LayoutElement layout = labelRect.GetComponent<LayoutElement>();
        layout.minHeight = minHeight;
        layout.preferredHeight = minHeight;
        layout.flexibleWidth = 1f;

        TextMeshProUGUI label = labelRect.gameObject.AddComponent<TextMeshProUGUI>();
        if (templateButtonText != null)
        {
            label.font = templateButtonText.font;
            label.fontSharedMaterial = templateButtonText.fontSharedMaterial;
            label.color = templateButtonText.color;
        }
        else
        {
            label.color = Color.white;
        }

        label.enableAutoSizing = false;
        label.fontSize = fontSize;
        label.alignment = alignment;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.text = text;
        return label;
    }

    private TMP_InputField CreateInput(Transform parent)
    {
        RectTransform inputRect = new GameObject("SaveNameInput", typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(TMP_InputField)).GetComponent<RectTransform>();
        inputRect.SetParent(parent, worldPositionStays: false);

        LayoutElement layout = inputRect.GetComponent<LayoutElement>();
        layout.minWidth = 260f;
        layout.preferredWidth = 420f;
        layout.flexibleWidth = 1f;
        layout.minHeight = 42f;
        layout.preferredHeight = 42f;

        Image bg = inputRect.GetComponent<Image>();
        bg.color = new Color(0.06f, 0.10f, 0.13f, 1f);
        Outline outline = inputRect.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.25f, 0.33f, 0.38f, 0.46f);
        outline.effectDistance = new Vector2(1f, -1f);
        outline.useGraphicAlpha = true;

        RectTransform textArea = new GameObject("TextArea", typeof(RectTransform)).GetComponent<RectTransform>();
        textArea.SetParent(inputRect, worldPositionStays: false);
        textArea.anchorMin = Vector2.zero;
        textArea.anchorMax = Vector2.one;
        textArea.offsetMin = new Vector2(10f, 6f);
        textArea.offsetMax = new Vector2(-10f, -6f);

        TextMeshProUGUI textComponent = CreateTextElement(textArea, "InputText");
        textComponent.alignment = TextAlignmentOptions.Left;
        textComponent.text = string.Empty;

        TextMeshProUGUI placeholderComponent = CreateTextElement(textArea, "Placeholder");
        placeholderComponent.alignment = TextAlignmentOptions.Left;
        placeholderComponent.text = "Save name";
        placeholderComponent.color = new Color(0.7f, 0.8f, 0.9f, 0.55f);

        TMP_InputField inputField = inputRect.GetComponent<TMP_InputField>();
        inputField.textComponent = textComponent;
        inputField.placeholder = placeholderComponent;
        inputField.lineType = TMP_InputField.LineType.SingleLine;
        inputField.characterLimit = 64;

        return inputField;
    }

    private TextMeshProUGUI CreateTextElement(Transform parent, string name)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, worldPositionStays: false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        if (templateButtonText != null)
        {
            text.font = templateButtonText.font;
            text.fontSharedMaterial = templateButtonText.fontSharedMaterial;
            text.color = templateButtonText.color;
        }
        else
        {
            text.color = Color.white;
        }

        text.enableAutoSizing = false;
        text.fontSize = 20f;
        text.richText = false;
        return text;
    }

    private Button CreateButton(Transform parent, string label, Action action, float minWidth)
    {
        return CreateButton(parent, label, action, minWidth, ButtonVisualStyle.Secondary);
    }

    private Button CreateButton(Transform parent, string label, Action action, float minWidth, ButtonVisualStyle style)
    {
        GameObject buttonObject;
        Button button;
        float preferredHeight = 40f;
        if (templateButton != null)
        {
            buttonObject = UnityEngine.Object.Instantiate(templateButton.gameObject, parent, worldPositionStays: false);
            button = buttonObject.GetComponent<Button>();
            RemoveLocalizedTextComponents(buttonObject);
            RectTransform templateRect = templateButton.transform as RectTransform;
            if (templateRect != null && templateRect.rect.height > 0f)
            {
                preferredHeight = Mathf.Clamp(templateRect.rect.height, 34f, 54f);
            }
        }
        else
        {
            buttonObject = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.SetParent(parent, worldPositionStays: false);
            Image fallbackImage = buttonObject.GetComponent<Image>();
            fallbackImage.color = new Color(0.14f, 0.36f, 0.31f, 0.98f);
            fallbackImage.type = Image.Type.Sliced;
            button = buttonObject.GetComponent<Button>();
        }

        ApplyButtonVisualStyle(button, style);

        LayoutElement layout = buttonObject.GetComponent<LayoutElement>();
        if (layout == null)
        {
            layout = buttonObject.AddComponent<LayoutElement>();
        }

        layout.minWidth = Mathf.Max(80f, minWidth * 0.7f);
        layout.preferredWidth = minWidth;
        layout.minHeight = preferredHeight;
        layout.preferredHeight = preferredHeight;
        layout.flexibleWidth = 0f;

        SetButtonText(buttonObject, label);
        ApplyButtonLabelStyle(buttonObject, style);

        Navigation navigation = button.navigation;
        navigation.mode = Navigation.Mode.None;
        button.navigation = navigation;

        button.onClick.RemoveAllListeners();
        if (action != null)
        {
            RectTransform buttonRect = buttonObject.transform as RectTransform;
            button.onClick.AddListener(() =>
            {
                if (EventSystem.current != null)
                {
                    EventSystem.current.SetSelectedGameObject(null);
                }

                PlayButtonFeedback(buttonRect);
                action();
            });
        }

        AttachButtonTextColorState(buttonObject, button);
        return button;
    }

    private void ApplyButtonLabelStyle(GameObject buttonObject, ButtonVisualStyle style)
    {
        if (buttonObject == null)
        {
            return;
        }

        Color labelColor = GetButtonLabelColor(style);

        TMP_FontAsset templateFont = templateButtonText != null ? templateButtonText.font : null;
        Material templateFontMaterial = templateButtonText != null ? templateButtonText.fontSharedMaterial : null;
        FontStyles templateFontStyle = templateButtonText != null
            ? (templateButtonText.fontStyle & ~FontStyles.Bold)
            : FontStyles.Normal;

        TMP_Text[] tmpLabels = buttonObject.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < tmpLabels.Length; i++)
        {
            TMP_Text tmpLabel = tmpLabels[i];
            if (tmpLabel == null)
            {
                continue;
            }

            if (templateFont != null)
            {
                tmpLabel.font = templateFont;
            }

            if (templateFontMaterial != null)
            {
                tmpLabel.fontSharedMaterial = templateFontMaterial;
            }

            tmpLabel.color = labelColor;
            tmpLabel.fontStyle = templateFontStyle;
            tmpLabel.enableAutoSizing = true;
            tmpLabel.fontSizeMin = 11f;
            tmpLabel.fontSizeMax = 18f;
            tmpLabel.textWrappingMode = TextWrappingModes.NoWrap;
            tmpLabel.overflowMode = TextOverflowModes.Ellipsis;
            tmpLabel.alignment = TextAlignmentOptions.Center;
        }

        Text[] uiLabels = buttonObject.GetComponentsInChildren<Text>(true);
        for (int i = 0; i < uiLabels.Length; i++)
        {
            Text uiLabel = uiLabels[i];
            if (uiLabel == null)
            {
                continue;
            }

            uiLabel.color = labelColor;
            uiLabel.fontStyle = FontStyle.Normal;
            uiLabel.resizeTextForBestFit = true;
            uiLabel.resizeTextMinSize = 11;
            uiLabel.resizeTextMaxSize = 18;
            uiLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            uiLabel.verticalOverflow = VerticalWrapMode.Truncate;
            uiLabel.alignment = TextAnchor.MiddleCenter;
        }
    }

    private static Color GetButtonLabelColor(ButtonVisualStyle style)
    {
        return Color.white;
    }

    private static void AttachButtonTextColorState(GameObject buttonObject, Button button)
    {
        if (buttonObject == null || button == null)
        {
            return;
        }

        ButtonTextColorState state = buttonObject.GetComponent<ButtonTextColorState>();
        if (state == null)
        {
            state = buttonObject.AddComponent<ButtonTextColorState>();
        }

        state.Bind(button);
    }

    private static void RemoveLocalizedTextComponents(GameObject buttonObject)
    {
        if (buttonObject == null)
        {
            return;
        }

        MonoBehaviour[] behaviours = buttonObject.GetComponentsInChildren<MonoBehaviour>(true);
        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (behaviour == null)
            {
                continue;
            }

            if (string.Equals(behaviour.GetType().Name, "LocalizedText", StringComparison.Ordinal))
            {
                UnityEngine.Object.Destroy(behaviour);
            }
        }
    }

    private void ApplyButtonVisualStyle(Button button, ButtonVisualStyle style)
    {
        if (button == null)
        {
            return;
        }

        button.transition = Selectable.Transition.ColorTint;
        if (button.targetGraphic == null)
        {
            button.targetGraphic = button.GetComponent<Image>();
        }

        if (templateButton != null)
        {
            button.colors = templateButton.colors;
        }
        else
        {
            Graphic targetGraphic = button.targetGraphic;
            if (targetGraphic != null)
            {
                targetGraphic.color = Color.white;
            }

            ColorBlock colors = button.colors;
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            colors.normalColor = new Color(0.95f, 0.95f, 0.95f, 1f);
            colors.highlightedColor = new Color(1f, 1f, 1f, 1f);
            colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.62f, 0.62f, 0.62f, 0.84f);
            button.colors = colors;
        }

        // Use strong, clearly distinct palette so ribbon button roles are obvious.
        ColorBlock tinted = button.colors;
        tinted.colorMultiplier = 1f;
        tinted.fadeDuration = 0.08f;
        switch (style)
        {
            case ButtonVisualStyle.Primary:
                tinted.normalColor = new Color(0.54f, 0.82f, 0.49f, 1f);
                tinted.highlightedColor = new Color(0.61f, 0.88f, 0.56f, 1f);
                tinted.pressedColor = new Color(0.44f, 0.67f, 0.40f, 1f);
                tinted.selectedColor = tinted.highlightedColor;
                tinted.disabledColor = new Color(0.38f, 0.48f, 0.35f, 0.74f);
                break;
            case ButtonVisualStyle.Secondary:
                tinted.normalColor = new Color(0.44f, 0.66f, 0.88f, 1f);
                tinted.highlightedColor = new Color(0.52f, 0.74f, 0.95f, 1f);
                tinted.pressedColor = new Color(0.35f, 0.53f, 0.72f, 1f);
                tinted.selectedColor = tinted.highlightedColor;
                tinted.disabledColor = new Color(0.33f, 0.40f, 0.49f, 0.74f);
                break;
            case ButtonVisualStyle.Neutral:
                tinted.normalColor = new Color(0.70f, 0.70f, 0.70f, 1f);
                tinted.highlightedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
                tinted.pressedColor = new Color(0.58f, 0.58f, 0.58f, 1f);
                tinted.selectedColor = tinted.highlightedColor;
                tinted.disabledColor = new Color(0.44f, 0.44f, 0.44f, 0.74f);
                break;
            case ButtonVisualStyle.Danger:
                tinted.normalColor = new Color(0.86f, 0.40f, 0.40f, 1f);
                tinted.highlightedColor = new Color(0.92f, 0.48f, 0.48f, 1f);
                tinted.pressedColor = new Color(0.70f, 0.30f, 0.30f, 1f);
                tinted.selectedColor = tinted.highlightedColor;
                tinted.disabledColor = new Color(0.49f, 0.30f, 0.30f, 0.74f);
                break;
        }

        button.colors = tinted;
        if (button.targetGraphic != null)
        {
            button.targetGraphic.color = tinted.normalColor;
        }
    }

    private void SetButtonText(GameObject buttonObject, string text)
    {
        if (buttonObject == null)
        {
            return;
        }

        TMP_Text[] tmpTexts = buttonObject.GetComponentsInChildren<TMP_Text>(true);
        bool appliedAnyText = false;
        for (int i = 0; i < tmpTexts.Length; i++)
        {
            TMP_Text tmpText = tmpTexts[i];
            if (tmpText == null)
            {
                continue;
            }

            tmpText.text = text;
            tmpText.enableAutoSizing = true;
            tmpText.fontSizeMin = 11f;
            tmpText.fontSizeMax = 18f;
            tmpText.fontStyle &= ~FontStyles.Bold;
            tmpText.overflowMode = TextOverflowModes.Ellipsis;
            tmpText.textWrappingMode = TextWrappingModes.NoWrap;
            appliedAnyText = true;
        }

        Text[] uiTexts = buttonObject.GetComponentsInChildren<Text>(true);
        for (int i = 0; i < uiTexts.Length; i++)
        {
            Text uiText = uiTexts[i];
            if (uiText == null)
            {
                continue;
            }

            uiText.text = text;
            uiText.resizeTextForBestFit = true;
            uiText.resizeTextMinSize = 11;
            uiText.resizeTextMaxSize = 18;
            uiText.fontStyle = FontStyle.Normal;
            uiText.horizontalOverflow = HorizontalWrapMode.Overflow;
            uiText.verticalOverflow = VerticalWrapMode.Truncate;
            appliedAnyText = true;
        }

        if (appliedAnyText)
        {
            return;
        }

        RectTransform textRect = new GameObject("Text", typeof(RectTransform)).GetComponent<RectTransform>();
        textRect.SetParent(buttonObject.transform, worldPositionStays: false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        TextMeshProUGUI created = textRect.gameObject.AddComponent<TextMeshProUGUI>();
        if (templateButtonText != null)
        {
            created.font = templateButtonText.font;
            created.fontSharedMaterial = templateButtonText.fontSharedMaterial;
            created.color = templateButtonText.color;
        }
        else
        {
            created.color = Color.white;
        }

        created.alignment = TextAlignmentOptions.Center;
        created.enableAutoSizing = true;
        created.fontSizeMin = 11f;
        created.fontSizeMax = 18f;
        created.fontStyle = FontStyles.Normal;
        created.overflowMode = TextOverflowModes.Ellipsis;
        created.textWrappingMode = TextWrappingModes.NoWrap;
        created.text = text;

        ApplyButtonLabelStyle(buttonObject, ButtonVisualStyle.Secondary);
    }

    private static string GetDisplaySaveName(SaveListEntry entry)
    {
        if (entry != null && entry.metadata != null && !string.IsNullOrWhiteSpace(entry.metadata.saveName))
        {
            return PrettySaveName(entry.metadata.saveName.Trim());
        }

        if (entry == null || string.IsNullOrWhiteSpace(entry.fileName))
        {
            return "Unnamed Save";
        }

        string name = entry.fileName;
        if (name.EndsWith(".peaksave.json", StringComparison.OrdinalIgnoreCase))
        {
            name = name.Substring(0, name.Length - ".peaksave.json".Length);
        }
        else
        {
            name = Path.GetFileNameWithoutExtension(name);
        }

        return string.IsNullOrWhiteSpace(name) ? "Unnamed Save" : PrettySaveName(name);
    }

    private static string PrettySaveName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Unnamed Save";
        }

        string normalized = name.Trim();
        if (normalized.EndsWith(".peaksave.json", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.Substring(0, normalized.Length - ".peaksave.json".Length);
        }
        else if (normalized.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.Substring(0, normalized.Length - ".json".Length);
        }
        else if (normalized.EndsWith(".peaksave", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.Substring(0, normalized.Length - ".peaksave".Length);
        }

        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = "Unnamed Save";
        }

        if (normalized.StartsWith("quicksave_", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("quit_save_", StringComparison.OrdinalIgnoreCase))
        {
            return "Quick Save";
        }

        if (normalized.StartsWith("autosave", StringComparison.OrdinalIgnoreCase))
        {
            return "Autosave";
        }

        return normalized;
    }

    private static string FormatSavedTime(SaveListEntry entry)
    {
        if (entry == null)
        {
            return "Saved: -";
        }

        DateTime savedAt = entry.fileTime;
        if (entry.metadata != null && entry.metadata.savedAtUtc != default)
        {
            savedAt = entry.metadata.savedAtUtc.ToLocalTime();
        }

        return "Saved: " + savedAt.ToString("M/d/yyyy h:mm:ss tt", CultureInfo.InvariantCulture);
    }

    private static string FormatLevelDisplayName(SaveMetadata metadata)
    {
        if (metadata == null)
        {
            return "-";
        }

        if (metadata.levelNumber.HasValue)
        {
            return "Level " + metadata.levelNumber.Value.ToString(CultureInfo.InvariantCulture);
        }

        string levelName = metadata.levelName;
        if (string.IsNullOrWhiteSpace(levelName))
        {
            return "-";
        }

        const string prefix = "Level_";
        if (levelName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            string numericPart = levelName.Substring(prefix.Length);
            if (int.TryParse(numericPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out int levelNumber))
            {
                return "Level " + levelNumber.ToString(CultureInfo.InvariantCulture);
            }
        }

        return levelName.Replace('_', ' ');
    }

    private void RebuildSaveRows(string preferredPath = null)
    {
        if (saveListContent == null || plugin == null)
        {
            return;
        }

        for (int i = saveListContent.childCount - 1; i >= 0; i--)
        {
            UnityEngine.Object.Destroy(saveListContent.GetChild(i).gameObject);
        }

        displayedEntries.Clear();
        displayedSlotBackgrounds.Clear();
        displayedSlotOutlines.Clear();
        displayedSlotTitles.Clear();

        displayedEntries.AddRange(plugin.UiGetSaveEntries());
        if (displayedEntries.Count == 0)
        {
            selectedSaveIndex = -1;
            TextMeshProUGUI emptyLabel = CreateLabel(saveListContent, "No save files found.", 42f, TextAlignmentOptions.Left, 17);
            emptyLabel.color = templateButtonText != null ? templateButtonText.color : Color.white;
            HideSlotActionPanel();
            return;
        }

        int nextSelectedIndex = -1;
        if (!string.IsNullOrWhiteSpace(preferredPath))
        {
            nextSelectedIndex = displayedEntries.FindIndex(
                entry => string.Equals(entry.fullPath, preferredPath, StringComparison.OrdinalIgnoreCase)
            );
        }

        if (nextSelectedIndex < 0 && selectedSaveIndex >= 0 && selectedSaveIndex < displayedEntries.Count)
        {
            nextSelectedIndex = selectedSaveIndex;
        }

        if (nextSelectedIndex < 0)
        {
            nextSelectedIndex = 0;
        }

        for (int i = 0; i < displayedEntries.Count; i++)
        {
            BuildSaveSlot(i, displayedEntries[i]);
        }

        SelectSave(nextSelectedIndex);
    }

    private void BuildSaveSlot(int slotIndex, SaveListEntry entry)
    {
        RectTransform slot = new GameObject("SaveSlot", typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline), typeof(VerticalLayoutGroup), typeof(LayoutElement)).GetComponent<RectTransform>();
        slot.SetParent(saveListContent, worldPositionStays: false);

        Image slotBackground = slot.GetComponent<Image>();
        Outline slotOutline = slot.GetComponent<Outline>();
        slotOutline.effectDistance = new Vector2(1f, -1f);
        slotOutline.useGraphicAlpha = true;

        LayoutElement slotLayout = slot.GetComponent<LayoutElement>();
        float slotHeight = entry.isCompatible ? 128f : 148f;
        slotLayout.minHeight = slotHeight;
        slotLayout.preferredHeight = slotHeight;

        VerticalLayoutGroup slotGroup = slot.GetComponent<VerticalLayoutGroup>();
        slotGroup.spacing = 3f;
        slotGroup.padding = new RectOffset(12, 12, 9, 8);
        slotGroup.childControlHeight = true;
        slotGroup.childControlWidth = true;
        slotGroup.childForceExpandHeight = false;
        slotGroup.childForceExpandWidth = true;

        Button slotButton = slot.GetComponent<Button>();
        slotButton.transition = Selectable.Transition.ColorTint;
        slotButton.colors = new ColorBlock
        {
            normalColor = Color.white,
            highlightedColor = new Color(1f, 1f, 1f, 1f),
            pressedColor = new Color(0.92f, 0.92f, 0.92f, 1f),
            selectedColor = new Color(1f, 1f, 1f, 1f),
            disabledColor = Color.white,
            colorMultiplier = 1f,
            fadeDuration = 0.08f
        };
        Navigation navigation = slotButton.navigation;
        navigation.mode = Navigation.Mode.None;
        slotButton.navigation = navigation;
        slotButton.onClick.RemoveAllListeners();
        slotButton.onClick.AddListener(() =>
        {
            PlayButtonFeedback(slot);
            SelectSave(slotIndex);
        });

        RectTransform headerRow = CreateRow(slot, 28f);
        HorizontalLayoutGroup headerLayout = headerRow.GetComponent<HorizontalLayoutGroup>();
        headerLayout.spacing = 8f;
        headerLayout.childForceExpandWidth = false;
        headerLayout.childControlWidth = true;
        headerLayout.childControlHeight = true;

        string displayName = GetDisplaySaveName(entry);
        TextMeshProUGUI titleLabel = CreateLabel(headerRow, displayName, 28f, TextAlignmentOptions.Left, 19);
        titleLabel.fontStyle = FontStyles.Normal;
        LayoutElement titleLayout = titleLabel.GetComponent<LayoutElement>();
        if (titleLayout != null)
        {
            titleLayout.flexibleWidth = 1f;
        }

        RectTransform savedBadge = new GameObject("SavedBadge", typeof(RectTransform), typeof(Image), typeof(LayoutElement)).GetComponent<RectTransform>();
        savedBadge.SetParent(headerRow, worldPositionStays: false);
        Image badgeImage = savedBadge.GetComponent<Image>();
        badgeImage.color = entry.isCompatible ? new Color(0f, 0f, 0f, 0.30f) : new Color(0.16f, 0.10f, 0.06f, 0.38f);
        LayoutElement badgeLayout = savedBadge.GetComponent<LayoutElement>();
        badgeLayout.minWidth = 196f;
        badgeLayout.preferredWidth = 196f;
        badgeLayout.minHeight = 24f;
        badgeLayout.preferredHeight = 24f;
        string badgeText = FormatSavedTime(entry);
        if (badgeText.StartsWith("Saved: ", StringComparison.OrdinalIgnoreCase))
        {
            badgeText = badgeText.Substring("Saved: ".Length);
        }

        TextMeshProUGUI badgeLabel = CreateLabel(savedBadge, badgeText, 24f, TextAlignmentOptions.Center, 13);
        badgeLabel.color = templateButtonText != null ? templateButtonText.color : new Color(0.92f, 0.92f, 0.92f, 0.95f);

        string levelText = FormatLevelDisplayName(entry.metadata);
        string segmentText = SaveManagerPlugin.ToDisplaySegmentName(
            entry.metadata != null ? entry.metadata.currentSegmentName : "-",
            entry.metadata != null ? entry.metadata.biomeId : string.Empty
        );
        string seedText = entry.metadata != null ? entry.metadata.levelSeed.ToString(CultureInfo.InvariantCulture) : "-";
        string detail = entry.metadata != null
            ? $"Level: {levelText}   Segment: {segmentText}   Seed: {seedText}"
            : "UNKNOWN OR LEGACY SAVE FORMAT";
        TextMeshProUGUI detailLabel = CreateLabel(slot, detail, 22f, TextAlignmentOptions.Left, 14);
        detailLabel.color = templateButtonText != null ? templateButtonText.color : new Color(0.9f, 0.9f, 0.9f, 0.92f);

        string runDetail = entry.metadata != null
            ? SaveManagerPlugin.FormatRunSummary(entry.metadata)
            : "Run: Day -  Time: -  Duration: -";
        TextMeshProUGUI runLabel = CreateLabel(slot, runDetail, 21f, TextAlignmentOptions.Left, 13);
        runLabel.color = templateButtonText != null ? templateButtonText.color : new Color(0.86f, 0.86f, 0.86f, 0.9f);

        if (!entry.isCompatible)
        {
            RectTransform warningRow = new GameObject("IncompatibleBanner", typeof(RectTransform), typeof(Image), typeof(LayoutElement)).GetComponent<RectTransform>();
            warningRow.SetParent(slot, worldPositionStays: false);
            Image warningImage = warningRow.GetComponent<Image>();
            warningImage.color = new Color(0.26f, 0.16f, 0.08f, 0.72f);
            LayoutElement warningLayout = warningRow.GetComponent<LayoutElement>();
            warningLayout.minHeight = 24f;
            warningLayout.preferredHeight = 24f;
            TextMeshProUGUI warningLabel = CreateLabel(
                warningRow,
                $"INCOMPATIBLE - {entry.incompatibilityReason}",
                22f,
                TextAlignmentOptions.Left,
                12
            );
            warningLabel.color = new Color(1f, 0.87f, 0.55f, 0.98f);
        }

        displayedSlotBackgrounds.Add(slotBackground);
        displayedSlotOutlines.Add(slotOutline);
        displayedSlotTitles.Add(titleLabel);
    }

    private void SelectSave(int slotIndex)
    {
        if (displayedEntries.Count == 0)
        {
            selectedSaveIndex = -1;
            HideSlotActionPanel();
            return;
        }

        selectedSaveIndex = Mathf.Clamp(slotIndex, 0, displayedEntries.Count - 1);
        RefreshSlotSelectionState();
        ShowSlotActionPanel();
    }

    private void RefreshSlotSelectionState()
    {
        for (int i = 0; i < displayedEntries.Count; i++)
        {
            SaveListEntry entry = displayedEntries[i];
            bool selected = i == selectedSaveIndex;
            Image slotBackground = i < displayedSlotBackgrounds.Count ? displayedSlotBackgrounds[i] : null;
            Outline slotOutline = i < displayedSlotOutlines.Count ? displayedSlotOutlines[i] : null;
            TextMeshProUGUI slotTitle = i < displayedSlotTitles.Count ? displayedSlotTitles[i] : null;
            if (slotBackground != null)
            {
                if (selected)
                {
                    slotBackground.color = entry.isCompatible
                        ? new Color(0.13f, 0.14f, 0.17f, 0.95f)
                        : new Color(0.22f, 0.16f, 0.11f, 0.95f);
                }
                else
                {
                    slotBackground.color = entry.isCompatible
                        ? new Color(0.08f, 0.09f, 0.11f, 0.88f)
                        : new Color(0.16f, 0.12f, 0.08f, 0.86f);
                }
            }

            if (slotOutline != null)
            {
                if (selected)
                {
                    slotOutline.effectColor = entry.isCompatible
                        ? new Color(0.90f, 0.90f, 0.90f, 0.45f)
                        : new Color(0.92f, 0.82f, 0.64f, 0.48f);
                }
                else
                {
                    slotOutline.effectColor = entry.isCompatible
                        ? new Color(0.55f, 0.55f, 0.55f, 0.22f)
                        : new Color(0.60f, 0.50f, 0.34f, 0.22f);
                }
            }

            if (slotTitle != null)
            {
                slotTitle.color = selected
                    ? (templateButtonText != null ? templateButtonText.color : Color.white)
                    : new Color(0.90f, 0.90f, 0.90f, 0.95f);
            }
        }
    }

    private SaveListEntry GetSelectedEntry()
    {
        if (selectedSaveIndex < 0 || selectedSaveIndex >= displayedEntries.Count)
        {
            return null;
        }

        return displayedEntries[selectedSaveIndex];
    }

    private void ShowSlotActionPanel()
    {
        if (slotActionPanel == null)
        {
            return;
        }

        SaveListEntry selectedEntry = GetSelectedEntry();
        if (selectedEntry == null)
        {
            return;
        }

        UpdateSlotActionPanelState();
        slotActionPanel.transform.SetAsLastSibling();
        slotActionPanel.SetActive(true);
    }

    private void HideSlotActionPanel()
    {
        if (slotActionPanel != null)
        {
            slotActionPanel.SetActive(false);
        }
    }

    private void UpdateSlotActionPanelState()
    {
        if (slotActionPanel == null || !slotActionPanel.activeSelf)
        {
            return;
        }

        SaveListEntry selectedEntry = GetSelectedEntry();
        if (selectedEntry == null)
        {
            HideSlotActionPanel();
            return;
        }

        bool isBusy = plugin != null && plugin.UiIsLoading();
        bool canSave = plugin != null && plugin.UiCanSaveNow(showReason: false);
        bool pendingQuit = plugin != null && plugin.UiHasPendingQuitSaveDecision();
        string displayName = GetDisplaySaveName(selectedEntry);

        if (slotActionLabel != null)
        {
            if (pendingQuit)
            {
                slotActionLabel.text = $"'{displayName}'\nChoose how to finish Quit & Save.";
            }
            else if (selectedEntry.isCompatible)
            {
                slotActionLabel.text = $"'{displayName}'\nChoose an action for this save.";
            }
            else
            {
                string reason = string.IsNullOrWhiteSpace(selectedEntry.incompatibilityReason)
                    ? "Incompatible save format."
                    : selectedEntry.incompatibilityReason;
                slotActionLabel.text = $"'{displayName}'\nINCOMPATIBLE SAVE: {reason}";
            }
        }

        if (loadSelectedButton != null)
        {
            SetButtonText(loadSelectedButton.gameObject, pendingQuit ? "OVERWRITE & QUIT" : "LOAD");
            ApplyButtonLabelStyle(loadSelectedButton.gameObject, pendingQuit ? ButtonVisualStyle.Danger : ButtonVisualStyle.Primary);
            ApplyButtonVisualStyle(loadSelectedButton, pendingQuit ? ButtonVisualStyle.Danger : ButtonVisualStyle.Primary);
            loadSelectedButton.interactable = pendingQuit
                ? (!isBusy && canSave)
                : (!isBusy && selectedEntry.isCompatible);
        }

        if (overwriteSelectedButton != null)
        {
            SetButtonText(overwriteSelectedButton.gameObject, pendingQuit ? "SAVE & QUIT" : "OVERWRITE");
            ApplyButtonLabelStyle(overwriteSelectedButton.gameObject, ButtonVisualStyle.Secondary);
            ApplyButtonVisualStyle(overwriteSelectedButton, ButtonVisualStyle.Secondary);
            overwriteSelectedButton.interactable = !isBusy && canSave;
        }

        if (deleteSelectedButton != null)
        {
            bool showDeleteAction = !pendingQuit;
            deleteSelectedButton.gameObject.SetActive(showDeleteAction);
            if (showDeleteAction)
            {
                SetButtonText(deleteSelectedButton.gameObject, "DELETE");
                ApplyButtonLabelStyle(deleteSelectedButton.gameObject, ButtonVisualStyle.Danger);
                ApplyButtonVisualStyle(deleteSelectedButton, ButtonVisualStyle.Danger);
                deleteSelectedButton.interactable = !isBusy;
            }
        }
    }

    private void LoadSelectedClicked()
    {
        if (plugin == null)
        {
            return;
        }

        if (plugin.UiHasPendingQuitSaveDecision())
        {
            OverwriteAndQuitClicked();
            return;
        }

        SaveListEntry selectedEntry = GetSelectedEntry();
        if (selectedEntry == null || !selectedEntry.isCompatible)
        {
            return;
        }

        HideSlotActionPanel();
        plugin.UiLoadSave(selectedEntry.fullPath);
    }

    private void OverwriteSelectedClicked()
    {
        if (plugin == null)
        {
            return;
        }

        if (plugin.UiHasPendingQuitSaveDecision())
        {
            HideSlotActionPanel();
            SaveAndQuitClicked();
            return;
        }

        SaveListEntry selectedEntry = GetSelectedEntry();
        if (selectedEntry == null)
        {
            return;
        }

        string displayName = GetDisplaySaveName(selectedEntry);
        string saveNameHint = selectedEntry.metadata != null && !string.IsNullOrWhiteSpace(selectedEntry.metadata.saveName)
            ? selectedEntry.metadata.saveName
            : displayName;

        ShowConfirmation(
            $"Overwrite '{displayName}' with current run?\nThis will replace the existing save.",
            () =>
            {
                plugin.UiOverwriteSave(selectedEntry.fullPath, saveNameHint);
                RefreshFromSource();
            }
        );
        HideSlotActionPanel();
    }

    private void DeleteSelectedClicked()
    {
        if (plugin == null)
        {
            return;
        }

        if (plugin.UiHasPendingQuitSaveDecision())
        {
            HideSlotActionPanel();
            CancelQuitClicked();
            return;
        }

        SaveListEntry selectedEntry = GetSelectedEntry();
        if (selectedEntry == null)
        {
            return;
        }

        string displayName = GetDisplaySaveName(selectedEntry);
        ShowConfirmation(
            $"Delete '{displayName}' permanently?\nThis cannot be undone.",
            () =>
            {
                plugin.UiDeleteSave(selectedEntry.fullPath);
                RefreshFromSource();
            }
        );
        HideSlotActionPanel();
    }

    private void QuickSaveClicked()
    {
        if (plugin == null)
        {
            return;
        }

        if (!plugin.UiCanSaveNow(showReason: true))
        {
            UpdateStatusLabel();
            return;
        }

        plugin.UiQuickSave();
        RefreshFromSource();
    }

    private void SaveClicked()
    {
        if (plugin == null)
        {
            return;
        }

        if (!plugin.UiCanSaveNow(showReason: true))
        {
            UpdateStatusLabel();
            return;
        }

        OpenNameEntryPanel("ENTER SAVE NAME", string.Empty, name =>
        {
            plugin.UiNamedSave(name);
        });
    }

    private void GoBack()
    {
        HideSlotActionPanel();
        HideConfirmation();
        HideNameEntryPanel();
        if (plugin != null && plugin.UiHasPendingQuitSaveDecision())
        {
            plugin.UiCancelPendingQuit();
        }

        if (handler != null && returnPage != null)
        {
            handler.TransistionToPage(returnPage, new SetActivePageTransistion());
        }
    }

    private void ShowConfirmation(string message, Action action)
    {
        confirmAction = action;
        if (confirmLabel != null)
        {
            confirmLabel.text = message;
        }

        if (confirmPanel != null)
        {
            confirmPanel.transform.SetAsLastSibling();
            confirmPanel.SetActive(true);
        }
    }

    private void ConfirmActionClicked()
    {
        Action action = confirmAction;
        HideConfirmation();
        action?.Invoke();
    }

    private void HideConfirmation()
    {
        confirmAction = null;
        if (confirmPanel != null)
        {
            confirmPanel.SetActive(false);
        }
    }

    private void OpenNameEntryPanel(string title, string initialValue, Action<string> onConfirm)
    {
        if (nameEntryPanel == null)
        {
            onConfirm?.Invoke(initialValue);
            RefreshFromSource();
            return;
        }

        nameEntryConfirmAction = onConfirm;
        if (nameEntryTitle != null)
        {
            nameEntryTitle.text = string.IsNullOrWhiteSpace(title) ? "NAME SAVE" : title;
        }

        if (nameEntryInput != null)
        {
            nameEntryInput.text = initialValue ?? string.Empty;
        }

        nameEntryPanel.SetActive(true);
        nameEntryPanel.transform.SetAsLastSibling();

        if (nameEntryInput != null)
        {
            nameEntryInput.ActivateInputField();
            nameEntryInput.Select();
        }
    }

    private void ConfirmNameEntry()
    {
        string value = nameEntryInput != null ? nameEntryInput.text : string.Empty;
        Action<string> callback = nameEntryConfirmAction;
        HideNameEntryPanel();
        callback?.Invoke(value);
        RefreshFromSource();
    }

    private void HideNameEntryPanel()
    {
        nameEntryConfirmAction = null;
        if (nameEntryPanel != null)
        {
            nameEntryPanel.SetActive(false);
        }
    }

    private void UpdateStatusLabel()
    {
        if (statusLabel == null || plugin == null)
        {
            return;
        }

        string status = plugin.UiCurrentStatus();
        if (string.IsNullOrWhiteSpace(status))
        {
            if (plugin.UiHasPendingQuitSaveDecision())
            {
                statusLabel.text = "Select a save slot for Overwrite & Quit, or use Save & Quit / Cancel Quit.";
            }
            else
            {
                statusLabel.text = "Select a save slot to open Load, Overwrite, or Delete options.";
            }
        }
        else
        {
            statusLabel.text = status;
        }
    }

    private void UpdateQuitDecisionRow()
    {
        if (quitDecisionRow == null || plugin == null)
        {
            return;
        }

        bool hasPendingQuitDecision = plugin.UiHasPendingQuitSaveDecision();
        quitDecisionRow.gameObject.SetActive(hasPendingQuitDecision);
        if (standardSaveRow != null)
        {
            standardSaveRow.gameObject.SetActive(!hasPendingQuitDecision);
        }

    }

    private void QuickSaveAndQuitClicked()
    {
        if (plugin == null)
        {
            return;
        }

        plugin.UiQuickSaveAndQuit();
        UpdateQuitDecisionRow();
        UpdateStatusLabel();
    }

    private void SaveAndQuitClicked()
    {
        if (plugin == null)
        {
            return;
        }

        OpenNameEntryPanel("ENTER SAVE NAME", string.Empty, name =>
        {
            plugin.UiNamedSaveAndQuit(name);
        });
        UpdateQuitDecisionRow();
        UpdateStatusLabel();
    }

    private void QuitWithoutSaveClicked()
    {
        if (plugin == null)
        {
            return;
        }

        plugin.UiQuitWithoutSaving();
        UpdateQuitDecisionRow();
    }

    private void CancelQuitClicked()
    {
        if (plugin == null)
        {
            return;
        }

        plugin.UiCancelPendingQuit();
        UpdateQuitDecisionRow();
    }

    private void OverwriteAndQuitClicked()
    {
        if (plugin == null)
        {
            return;
        }

        SaveListEntry selectedEntry = GetSelectedEntry();
        if (selectedEntry == null)
        {
            if (statusLabel != null)
            {
                statusLabel.text = "Select a save slot, then click Overwrite & Quit.";
            }

            return;
        }

        string displayName = GetDisplaySaveName(selectedEntry);
        string saveNameHint = selectedEntry.metadata != null && !string.IsNullOrWhiteSpace(selectedEntry.metadata.saveName)
            ? selectedEntry.metadata.saveName
            : displayName;

        ShowConfirmation($"Overwrite '{displayName}' and quit to menu?\nThis will replace the existing save.", () =>
        {
            plugin.UiOverwriteSave(selectedEntry.fullPath, saveNameHint);
            if (plugin.UiHasPendingQuitSaveDecision())
            {
                RefreshFromSource();
            }
        });
    }
}

internal sealed class ButtonTextColorState : MonoBehaviour
{
    private static readonly Color EnabledColor = Color.white;

    private static readonly Color DisabledColor = Color.black;

    private Button button;

    private TMP_Text[] tmpTexts = Array.Empty<TMP_Text>();

    private Text[] uiTexts = Array.Empty<Text>();

    private bool hasLastState;

    private bool lastInteractable;

    internal void Bind(Button targetButton)
    {
        button = targetButton;
        RefreshCache();
        Apply(force: true);
    }

    private void Awake()
    {
        if (button == null)
        {
            button = GetComponent<Button>();
        }

        RefreshCache();
    }

    private void OnEnable()
    {
        Apply(force: true);
    }

    private void LateUpdate()
    {
        Apply(force: false);
    }

    private void RefreshCache()
    {
        tmpTexts = GetComponentsInChildren<TMP_Text>(includeInactive: true);
        uiTexts = GetComponentsInChildren<Text>(includeInactive: true);
    }

    private void Apply(bool force)
    {
        if (button == null)
        {
            return;
        }

        bool interactable = button.IsInteractable();
        if (!force && hasLastState && interactable == lastInteractable)
        {
            return;
        }

        hasLastState = true;
        lastInteractable = interactable;
        Color color = interactable ? EnabledColor : DisabledColor;

        if (tmpTexts != null)
        {
            for (int i = 0; i < tmpTexts.Length; i++)
            {
                TMP_Text label = tmpTexts[i];
                if (label != null)
                {
                    label.color = color;
                }
            }
        }

        if (uiTexts != null)
        {
            for (int i = 0; i < uiTexts.Length; i++)
            {
                Text label = uiTexts[i];
                if (label != null)
                {
                    label.color = color;
                }
            }
        }
    }
}

[HarmonyPatch(typeof(PauseMenuMainPage), "Start")]
internal static class PauseMenuMainPageStartPatch
{
    private static void Postfix(PauseMenuMainPage __instance)
    {
        SaveManagerPlugin.TryAttachPauseMenuButton(__instance);
    }
}

[HarmonyPatch(typeof(PauseMenuMainPage), "OnEnable")]
internal static class PauseMenuMainPageOnEnablePatch
{
    private static void Postfix(PauseMenuMainPage __instance)
    {
        SaveManagerPlugin.TryAttachPauseMenuButton(__instance);
    }
}

[HarmonyPatch(typeof(PauseMenuMainPage), "OnQuitClicked")]
internal static class PauseMenuMainPageOnQuitClickedPatch
{
    private static bool Prefix(PauseMenuMainPage __instance)
    {
        return !SaveManagerPlugin.TryInterceptPauseQuit(__instance);
    }
}

[HarmonyPatch(typeof(LevelGeneration), "Generate")]
internal static class LevelGenerationGeneratePatch
{
    private static void Prefix(LevelGeneration __instance)
    {
        if (!SaveManagerPlugin.TryGetPendingSeed(out int seed))
        {
            return;
        }

        __instance.seed = seed;
        UnityEngine.Random.InitState(seed);
    }
}

[HarmonyPatch(typeof(LevelGeneration), "RandomizeBiomeVariants")]
internal static class LevelGenerationRandomizeBiomeVariantsPatch
{
    private static void Prefix()
    {
        if (!SaveManagerPlugin.TryGetPendingSeed(out int seed))
        {
            return;
        }

        UnityEngine.Random.InitState(seed);
    }
}
