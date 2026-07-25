using Newtonsoft.Json;
using PEAKSaveManager;
using UnityEngine;

internal static class Program
{
    private static readonly Dictionary<string, string[]> DeployableCategoryKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["piton"] = new[] { "Piton", "PitonLegacy" },
        ["rope"] = new[] { "Rope", "RopeAnchor", "RopeAnchorWithRope" },
        ["chain"] = new[] { "ChainLauncher" },
        ["scout"] = new[] { "ScoutCannon" },
        ["bean"] = new[] { "MagicBeanVine" },
        ["fungus"] = new[] { "CloudFungus", "BounceFungus", "ShelfShroom" },
        ["checkpoint"] = new[] { "CheckpointFlag", "CheckpointConstructable" },
        ["stove"] = new[] { "PortableStove" }
    };

    private static int Main()
    {
        try
        {
            SaveEnvelope original = CreateSampleEnvelope();
            string json = JsonConvert.SerializeObject(original, Formatting.Indented);
            SaveEnvelope loaded = JsonConvert.DeserializeObject<SaveEnvelope>(json);
            Assert(loaded != null, "Deserialized envelope is null.");

            VerifyDeployableCoverage(loaded.worldObjects);
            VerifyCheckpointRoundTrip(loaded.worldObjects);
            VerifyItemDataRoundTrip(loaded.players);
            VerifyVelocityRoundTrip(loaded.players, loaded.groundItems);

            Console.WriteLine("Save/load verification passed for deployable categories: piton, rope, chain, scout, bean, fungus, checkpoint, stove.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Save/load verification failed: " + ex.Message);
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static SaveEnvelope CreateSampleEnvelope()
    {
        SaveEnvelope envelope = new SaveEnvelope
        {
            metadata = new SaveMetadata
            {
                saveName = "verification",
                savedAtUtc = DateTime.UtcNow,
                sceneName = "Level_14",
                levelName = "Level_14",
                levelNumber = 14,
                levelSeed = 12345,
                currentSegment = 2,
                currentSegmentName = "Roots",
                playerCount = 1
            }
        };

        ItemSlotSnapshot slot = new ItemSlotSnapshot
        {
            itemId = 101,
            itemUses = 2,
            dataEntries =
            {
                new ItemDataEntrySnapshot
                {
                    keyName = "ItemUses",
                    keyValue = 0,
                    valueType = "IntItemData",
                    intValue = 2,
                    hasValue = true
                },
                new ItemDataEntrySnapshot
                {
                    keyName = "Used",
                    keyValue = 3,
                    valueType = "BoolItemData",
                    boolValue = true,
                    hasValue = true
                }
            }
        };

        envelope.players.Add(new PlayerSnapshot
        {
            playerName = "Host",
            actorNumber = 1,
            position = new Vector3Snapshot(new Vector3(12f, 34f, 56f)),
            rotation = new Vector3Snapshot(new Vector3(0f, 180f, 0f)),
            velocity = new Vector3Snapshot(new Vector3(1.2f, -0.3f, 0.5f)),
            angularVelocity = new Vector3Snapshot(new Vector3(0.1f, 0.2f, 0.3f)),
            inventory = new InventorySnapshot
            {
                mainSlots = { slot },
                heldItemId = 101,
                selectedSlotId = 0,
                equippedMainSlotIndex = 0
            }
        });

        envelope.groundItems.Add(new GroundItemSnapshot
        {
            itemId = 202,
            objectName = "GroundItem",
            objectPath = "World/GroundItem",
            position = new Vector3Snapshot(new Vector3(4f, 2f, 1f)),
            rotation = new Vector3Snapshot(new Vector3(0f, 90f, 0f)),
            velocity = new Vector3Snapshot(new Vector3(3f, 2f, 1f)),
            angularVelocity = new Vector3Snapshot(new Vector3(0.5f, 0.2f, 0.1f))
        });

        envelope.worldObjects.Add(new WorldObjectSnapshot { kind = "Piton", objectName = "Piton", objectPath = "World/Piton", position = new Vector3Snapshot(new Vector3(1f, 2f, 3f)), rotation = new Vector3Snapshot(new Vector3(0f, 0f, 0f)) });
        envelope.worldObjects.Add(new WorldObjectSnapshot { kind = "Rope", objectName = "Rope", objectPath = "World/Rope", position = new Vector3Snapshot(new Vector3(2f, 3f, 4f)), rotation = new Vector3Snapshot(new Vector3(0f, 30f, 0f)), boolA = true, floatA = 9f });
        envelope.worldObjects.Add(new WorldObjectSnapshot { kind = "ChainLauncher", objectName = "ChainLauncher", objectPath = "World/Chain", position = new Vector3Snapshot(new Vector3(4f, 5f, 6f)), rotation = new Vector3Snapshot(new Vector3(0f, 45f, 0f)), boolA = true, floatA = 2f });
        envelope.worldObjects.Add(new WorldObjectSnapshot { kind = "ScoutCannon", objectName = "ScoutCannon", objectPath = "World/Scout", position = new Vector3Snapshot(new Vector3(5f, 6f, 7f)), rotation = new Vector3Snapshot(new Vector3(0f, 20f, 0f)), boolA = true });
        envelope.worldObjects.Add(new WorldObjectSnapshot { kind = "MagicBeanVine", objectName = "MagicBeanVine", objectPath = "World/Bean", position = new Vector3Snapshot(new Vector3(6f, 7f, 8f)), rotation = new Vector3Snapshot(new Vector3(0f, 10f, 0f)), floatA = 14f });
        envelope.worldObjects.Add(new WorldObjectSnapshot { kind = "CloudFungus", objectName = "CloudFungus", objectPath = "World/CloudFungus", position = new Vector3Snapshot(new Vector3(8f, 9f, 10f)), rotation = new Vector3Snapshot(new Vector3(0f, 0f, 0f)), boolA = true, floatA = 11f });
        envelope.worldObjects.Add(new WorldObjectSnapshot { kind = "ShelfShroom", objectName = "ShelfShroom", objectPath = "World/Shelf", position = new Vector3Snapshot(new Vector3(7f, 8f, 9f)), rotation = new Vector3Snapshot(new Vector3(0f, 0f, 0f)) });
        envelope.worldObjects.Add(new WorldObjectSnapshot
        {
            kind = "CheckpointFlag",
            objectName = "CheckpointFlag",
            objectPath = "World/Checkpoint",
            position = new Vector3Snapshot(new Vector3(3f, 3f, 3f)),
            rotation = new Vector3Snapshot(new Vector3(0f, 180f, 0f)),
            intA = 1,
            stringA = "Host",
            stringB = "World/Players/Host",
            floatListA = { 0.25f, 1f, 0.75f }
        });
        envelope.worldObjects.Add(new WorldObjectSnapshot { kind = "PortableStove", objectName = "PortableStove", objectPath = "World/Stove", position = new Vector3Snapshot(new Vector3(2f, 2f, 2f)), rotation = new Vector3Snapshot(new Vector3(0f, 5f, 0f)), boolA = true, floatA = 1f, floatB = 22f });

        return envelope;
    }

    private static void VerifyDeployableCoverage(List<WorldObjectSnapshot> worldObjects)
    {
        Assert(worldObjects != null && worldObjects.Count > 0, "World object list is empty.");

        HashSet<string> kinds = new(worldObjects
            .Where(snapshot => snapshot != null && !string.IsNullOrWhiteSpace(snapshot.kind))
            .Select(snapshot => snapshot.kind), StringComparer.OrdinalIgnoreCase);

        foreach ((string category, string[] categoryKinds) in DeployableCategoryKinds)
        {
            bool matched = categoryKinds.Any(kinds.Contains);
            Assert(matched, $"Missing deployable category coverage: {category}.");
        }
    }

    private static void VerifyCheckpointRoundTrip(List<WorldObjectSnapshot> worldObjects)
    {
        WorldObjectSnapshot checkpoint = worldObjects.FirstOrDefault(snapshot =>
            snapshot != null && string.Equals(snapshot.kind, "CheckpointFlag", StringComparison.OrdinalIgnoreCase));
        Assert(checkpoint != null, "CheckpointFlag snapshot missing.");
        Assert(checkpoint.intA == 1, "Checkpoint owner actor number did not round-trip.");
        Assert(string.Equals(checkpoint.stringA, "Host", StringComparison.Ordinal), "Checkpoint owner name did not round-trip.");
        Assert(checkpoint.floatListA != null && checkpoint.floatListA.Count == 3, "Checkpoint status array did not round-trip.");
    }

    private static void VerifyItemDataRoundTrip(List<PlayerSnapshot> players)
    {
        PlayerSnapshot player = players.FirstOrDefault();
        Assert(player != null, "Player snapshot missing.");

        ItemSlotSnapshot slot = player.inventory?.mainSlots?.FirstOrDefault();
        Assert(slot != null, "Primary item slot missing.");
        Assert(slot.dataEntries != null && slot.dataEntries.Count >= 2, "Generic item data entries did not round-trip.");
        Assert(slot.dataEntries.Any(entry => string.Equals(entry.keyName, "ItemUses", StringComparison.OrdinalIgnoreCase)), "ItemUses entry missing after round-trip.");
    }

    private static void VerifyVelocityRoundTrip(List<PlayerSnapshot> players, List<GroundItemSnapshot> groundItems)
    {
        PlayerSnapshot player = players.FirstOrDefault();
        Assert(player != null, "Player snapshot missing for velocity test.");
        Assert(player.velocity != null, "Player velocity snapshot missing.");

        GroundItemSnapshot groundItem = groundItems.FirstOrDefault();
        Assert(groundItem != null, "Ground item snapshot missing for velocity test.");
        Assert(groundItem.velocity != null && groundItem.angularVelocity != null, "Ground rigidbody velocity snapshot missing.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
