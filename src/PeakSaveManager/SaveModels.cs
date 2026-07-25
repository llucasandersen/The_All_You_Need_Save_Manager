using System;
using System.Collections.Generic;
using UnityEngine;

namespace PEAKSaveManager;

[Serializable]
public sealed class SaveEnvelope
{
    public const string SaveMagic = "PEAK_SAVE_MANAGER";
    public const int CurrentFormatVersion = 6;

    public string magic = SaveMagic;

    public int formatVersion = CurrentFormatVersion;

    public string pluginGuid;

    public string pluginVersion;

    public SaveMetadata metadata = new SaveMetadata();

    public List<PlayerSnapshot> players = new List<PlayerSnapshot>();

    public List<CampfireSnapshot> campfires = new List<CampfireSnapshot>();

    public List<LuggageSnapshot> luggageStates = new List<LuggageSnapshot>();

    public List<ContainerSnapshot> containerStates = new List<ContainerSnapshot>();

    public List<GroundItemSnapshot> groundItems = new List<GroundItemSnapshot>();

    public List<WorldObjectSnapshot> worldObjects = new List<WorldObjectSnapshot>();
}

[Serializable]
public sealed class SaveMetadata
{
    public string saveName;

    public DateTime savedAtUtc;

    public string sceneName;

    public string levelName;

    public int? levelNumber;

    public int? dailyLevelIndex;

    public string biomeId;

    public int levelSeed;

    public int currentSegment;

    public string currentSegmentName;

    public int ascent;

    public int playerCount;

    public int? runDay;

    public float? runTimeSeconds;

    public bool? runTimerActive;

    public float? timeOfDay;

    public string inGameTime;
}

[Serializable]
public sealed class PlayerSnapshot
{
    public string playerName;

    public int actorNumber;

    public Vector3Snapshot position = new Vector3Snapshot();

    public Vector3Snapshot rotation = new Vector3Snapshot();

    public Vector3Snapshot velocity = new Vector3Snapshot();

    public Vector3Snapshot angularVelocity = new Vector3Snapshot();

    public CharacterSnapshot character = new CharacterSnapshot();

    public InventorySnapshot inventory = new InventorySnapshot();
}

[Serializable]
public sealed class CharacterSnapshot
{
    public bool dead;

    public bool passedOut;

    public bool fullyPassedOut;

    public bool isGrounded;

    public bool isClimbing;

    public bool isRopeClimbing;

    public bool isVineClimbing;

    public bool isSprinting;

    public float currentStamina;

    public float extraStamina;

    public float sinceGrounded;

    public Vector2Snapshot lookValues = new Vector2Snapshot();

    public List<string> checkpointFlagPaths = new List<string>();

    public List<CharacterStatusSnapshot> statuses = new List<CharacterStatusSnapshot>();
}

[Serializable]
public sealed class CharacterStatusSnapshot
{
    public string statusType;

    public float amount;
}

[Serializable]
public sealed class InventorySnapshot
{
    public List<ItemSlotSnapshot> mainSlots = new List<ItemSlotSnapshot>();

    public ItemSlotSnapshot tempSlot = ItemSlotSnapshot.Empty();

    public bool hasBackpack;

    public List<ItemSlotSnapshot> backpackSlots = new List<ItemSlotSnapshot>();

    public int? selectedSlotId;

    public int? equippedMainSlotIndex;

    public bool equippedTempSlot;

    public int? equippedBackpackSlotIndex;

    public ushort heldItemId = ushort.MaxValue;
}

[Serializable]
public sealed class ItemSlotSnapshot
{
    public ushort itemId = ushort.MaxValue;

    public int? itemUses;

    public int? petterItemUses;

    public float? useRemainingPercentage;

    public bool? used;

    public float? fuel;

    public int? cookedAmount;

    public bool? flareActive;

    public float? screamTime;

    public bool? spawnedBees;

    public List<ItemDataEntrySnapshot> dataEntries = new List<ItemDataEntrySnapshot>();

    public static ItemSlotSnapshot Empty()
    {
        return new ItemSlotSnapshot
        {
            itemId = ushort.MaxValue
        };
    }

    public bool HasItem()
    {
        return itemId != ushort.MaxValue;
    }
}

[Serializable]
public sealed class CampfireSnapshot
{
    public int segmentIndex;

    public string campfireName;

    public int state;

    public float beenBurningFor;

    public int advanceToSegment;
}

[Serializable]
public sealed class LuggageSnapshot
{
    public string objectName;

    public string objectPath;

    public Vector3Snapshot position = new Vector3Snapshot();

    public int state;

    public bool isRespawnChest;

    public bool respawnChestSpent;

    public bool respawnChestRevivedPlayers;
}

[Serializable]
public sealed class ContainerSnapshot
{
    public string containerType;

    public string objectName;

    public string objectPath;

    public Vector3Snapshot position = new Vector3Snapshot();

    public int state;

    public bool boolA;

    public bool boolB;

    public float floatA;
}

[Serializable]
public sealed class GroundItemSnapshot
{
    public ushort itemId = ushort.MaxValue;

    public string objectName;

    public string objectPath;

    public Vector3Snapshot position = new Vector3Snapshot();

    public Vector3Snapshot rotation = new Vector3Snapshot();

    public Vector3Snapshot velocity = new Vector3Snapshot();

    public Vector3Snapshot angularVelocity = new Vector3Snapshot();

    public bool isKinematic;

    public int? itemUses;

    public int? petterItemUses;

    public float? useRemainingPercentage;

    public bool? used;

    public float? fuel;

    public int? cookedAmount;

    public bool? flareActive;

    public float? screamTime;

    public bool? spawnedBees;

    public List<ItemDataEntrySnapshot> dataEntries = new List<ItemDataEntrySnapshot>();
}

[Serializable]
public sealed class WorldObjectSnapshot
{
    public string kind;

    public string objectName;

    public string objectPath;

    public Vector3Snapshot position = new Vector3Snapshot();

    public Vector3Snapshot rotation = new Vector3Snapshot();

    public bool boolA;

    public float floatA;

    public float floatB;

    public int intA;

    public string stringA;

    public string stringB;

    public List<float> floatListA = new List<float>();
}

[Serializable]
public sealed class ItemDataEntrySnapshot
{
    public int keyValue;

    public string keyName;

    public string valueType;

    public bool hasValue;

    public int? intValue;

    public float? floatValue;

    public bool? boolValue;

    public string stringValue;

    public Vector4Snapshot colorValue = new Vector4Snapshot();

    public bool hasColorValue;

    public string serializedJson;

    public List<ItemSlotSnapshot> backpackSlots = new List<ItemSlotSnapshot>();
}

[Serializable]
public sealed class Vector3Snapshot
{
    public float x;

    public float y;

    public float z;

    public Vector3Snapshot()
    {
    }

    public Vector3Snapshot(Vector3 source)
    {
        x = source.x;
        y = source.y;
        z = source.z;
    }

    public Vector3 ToUnity()
    {
        return new Vector3(x, y, z);
    }
}

[Serializable]
public sealed class Vector2Snapshot
{
    public float x;

    public float y;

    public Vector2Snapshot()
    {
    }

    public Vector2Snapshot(Vector2 source)
    {
        x = source.x;
        y = source.y;
    }

    public Vector2 ToUnity()
    {
        return new Vector2(x, y);
    }
}

[Serializable]
public sealed class Vector4Snapshot
{
    public float x;

    public float y;

    public float z;

    public float w;

    public Vector4Snapshot()
    {
    }

    public Vector4Snapshot(Vector4 source)
    {
        x = source.x;
        y = source.y;
        z = source.z;
        w = source.w;
    }

    public Vector4 ToUnity()
    {
        return new Vector4(x, y, z, w);
    }
}

public sealed class SaveListEntry
{
    public string fileName;

    public string fullPath;

    public bool isCompatible;

    public string incompatibilityReason;

    public SaveMetadata metadata;

    public DateTime fileTime;

    public long fileSize;
}
