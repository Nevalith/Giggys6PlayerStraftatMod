using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Steamworks;
using FishNet; // Added for InstanceFinder
using FishNet.Managing; // Added for InstanceFinder
using FishNet.Connection;
using TMPro;
using System.Reflection.Emit; // Required for Transpiler
using System; // Required for using the 'Type' class in patches
using System.Reflection; // Required for accessing private fields

namespace SixPlayerMod
{
    public static class PluginInfo
    {
        public const string PLUGIN_GUID = "com.yourname.gamename.sixplayermod";
        public const string PLUGIN_NAME = "Six Player Mod";
        public const string PLUGIN_VERSION = "2.0.1"; // Incremented version for the fix
    }

    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class SixPlayerPlugin : BaseUnityPlugin
    {
        private readonly Harmony harmony = new Harmony(PluginInfo.PLUGIN_GUID);
        public static SixPlayerPlugin Instance { get; private set; }
        internal static ManualLogSource Log;

        public static ConfigEntry<int> MaxPlayersConfig;
        public static ConfigEntry<GameMode> GameModeConfig;

        public enum GameMode { FFA, Teams2v2, Teams3v3 }

        private void Awake()
        {
            if (Instance == null) Instance = this;
            Log = Logger;

            MaxPlayersConfig = Config.Bind("General", "MaxPlayers", 6, "The maximum number of players allowed in a lobby.");
            GameModeConfig = Config.Bind("Teams", "GameMode", GameMode.Teams3v3, "The team configuration for the match (FFA, 2v2, 3v3).");

            harmony.PatchAll();
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_NAME} version {PluginInfo.PLUGIN_VERSION} is loaded!");
        }
    }

    [HarmonyPatch]
    public class HarmonyPatches
    {
        private static bool hudResized = false;
        private static bool pedestalsResized = false;

        private static bool IsNumericDropdown(TMP_Dropdown dropdown)
        {
            if (dropdown == null || dropdown.options.Count == 0) return false;
            return int.TryParse(dropdown.options[0].text, out _);
        }

        private static void SyncAndUpdateAllMaxPlayerDropdowns(SteamLobby steamLobbyInstance, int newMaxPlayersValue)
        {
            int maxPlayersConfig = SixPlayerPlugin.MaxPlayersConfig.Value;
            List<TMP_Dropdown> dropdownsToUpdate = new List<TMP_Dropdown>();

            if (steamLobbyInstance.MaxPlayersDropdown != null) dropdownsToUpdate.Add(steamLobbyInstance.MaxPlayersDropdown);
            if (steamLobbyInstance.LobbyWindow != null)
            {
                var lobbyDropdowns = steamLobbyInstance.LobbyWindow.GetComponentsInChildren<TMP_Dropdown>(true);
                foreach (var dd in lobbyDropdowns)
                {
                    if (dd.gameObject.name.ToLower().Contains("maxplayers")) dropdownsToUpdate.Add(dd);
                }
            }

            foreach (var dropdown in dropdownsToUpdate.Distinct())
            {
                if (dropdown == null) continue;
                if (dropdown.options.Count != (maxPlayersConfig - 1))
                {
                    dropdown.ClearOptions();
                    List<string> options = new List<string>();
                    for (int i = 2; i <= maxPlayersConfig; i++) options.Add(i.ToString());
                    dropdown.AddOptions(options);
                }
                int newDropdownIndex = newMaxPlayersValue - 2;
                if (newDropdownIndex >= 0 && newDropdownIndex < dropdown.options.Count) dropdown.value = newDropdownIndex;
                dropdown.RefreshShownValue();
            }
        }

        private static void UpdateGamemodeDropdownUI(TMP_Dropdown dropdown)
        {
            if (dropdown == null || dropdown.options.Any(o => o.text == "3v3 Teams")) return;
            var originalValue = dropdown.value;
            dropdown.ClearOptions();
            dropdown.AddOptions(new List<string> { "FFA", "2v2 Teams", "3v3 Teams" });
            dropdown.value = (originalValue < dropdown.options.Count) ? originalValue : 2;
            dropdown.RefreshShownValue();
        }

        [HarmonyPatch(typeof(SteamLobby), "Start")]
        [HarmonyPostfix]
        public static void SteamLobbyStartPostfix(SteamLobby __instance)
        {
            int maxPlayers = SixPlayerPlugin.MaxPlayersConfig.Value;
            __instance.maxPlayers = maxPlayers;
            if (__instance.lobbyManager != null) __instance.lobbyManager.createArguments.slots = maxPlayers;
            SyncAndUpdateAllMaxPlayerDropdowns(__instance, __instance.maxPlayers);
            UpdateGamemodeDropdownUI(__instance.GamemodeDropdown);
            hudResized = false;
            pedestalsResized = false;
        }

        [HarmonyPatch(typeof(SteamLobby), "OnLobbyEntered")]
        [HarmonyPostfix]
        public static void OnLobbyEnteredPostfix()
        {
            if (pedestalsResized) return;
            var lobbyController = LobbyController.Instance;
            if (lobbyController == null) return;

            if (lobbyController.previews != null && lobbyController.previews.Length > 0)
            {
                int maxPlayers = SixPlayerPlugin.MaxPlayersConfig.Value;
                if (lobbyController.previews.Length < maxPlayers)
                {
                    var previewsField = typeof(LobbyController).GetField("previews", BindingFlags.Public | BindingFlags.Instance);
                    if (previewsField != null)
                    {
                        var resizedPreviews = ResizeUIArray(lobbyController.previews, maxPlayers, "PlayerPedestal");
                        previewsField.SetValue(lobbyController, resizedPreviews);
                        pedestalsResized = true;
                    }
                }
                else { pedestalsResized = true; }
            }
        }

        [HarmonyPatch(typeof(SteamLobby), "LeaveLobby")]
        [HarmonyPostfix]
        public static void LeaveLobbyPostfix() => pedestalsResized = false;

        [HarmonyPatch(typeof(SteamLobby), "SetMaxPlayers")]
        [HarmonyPrefix]
        public static bool SetMaxPlayersPrefix(SteamLobby __instance, TMP_Dropdown _dropdown)
        {
            int selectedMaxPlayers = _dropdown.value + 2;
            __instance.maxPlayers = selectedMaxPlayers;
            SyncAndUpdateAllMaxPlayerDropdowns(__instance, selectedMaxPlayers);
            if (__instance.lobbyManager != null) __instance.lobbyManager.createArguments.slots = selectedMaxPlayers;
            InstanceFinder.TransportManager.Transport.SetMaximumClients(selectedMaxPlayers - 1);
            if (__instance.inSteamLobby)
            {
                SteamMatchmaking.SetLobbyMemberLimit(new CSteamID(__instance.CurrentLobbyID), selectedMaxPlayers);
                __instance.UpdatePlayerCountDisplay();
                if (LobbyController.Instance?.LocalPlayerController != null)
                    LobbyController.Instance.LocalPlayerController.UpdateServerMaxPlayers();
            }
            return false;
        }

        [HarmonyPatch(typeof(SteamLobby), "SetGamemode", new Type[] { typeof(int) })]
        [HarmonyPrefix]
        public static bool SetGamemodePrefix(SteamLobby __instance, int value)
        {
            UpdateGamemodeDropdownUI(__instance.GamemodeDropdown);
            string gamemodeString = "FFA";
            bool isTeams = false;

            switch (value)
            {
                case 0: SixPlayerPlugin.GameModeConfig.Value = SixPlayerPlugin.GameMode.FFA; isTeams = false; break;
                case 1: SixPlayerPlugin.GameModeConfig.Value = SixPlayerPlugin.GameMode.Teams2v2; gamemodeString = "2v2 Teams"; isTeams = true; break;
                case 2: SixPlayerPlugin.GameModeConfig.Value = SixPlayerPlugin.GameMode.Teams3v3; gamemodeString = "3v3 Teams"; isTeams = true; break;
            }

            __instance.playingTeams = isTeams;
            if (InstanceFinder.NetworkManager.IsServer)
            {
                if (GameManager.Instance != null)
                {
                    // **FIXED**: Use reflection to set the property value, bypassing compiler issues.
                    var prop = typeof(GameManager).GetProperty("SyncAccessor_playingTeams");
                    if (prop != null)
                    {
                        prop.SetValue(GameManager.Instance, isTeams);
                    }
                    else
                    {
                        SixPlayerPlugin.Log.LogError("Could not find property 'SyncAccessor_playingTeams' on GameManager.");
                    }

                    ScoreManager.Instance.ResetTeams();

                    if (isTeams)
                    {
                        List<int> playerIds = ClientInstance.playerInstances.Keys.OrderBy(x => new System.Random().Next()).ToList();
                        int teamSize = (SixPlayerPlugin.GameModeConfig.Value == SixPlayerPlugin.GameMode.Teams2v2) ? 2 : 3;
                        for (int i = 0; i < playerIds.Count; i++)
                        {
                            if (ScoreManager.Instance != null) ScoreManager.Instance.SetTeamId(playerIds[i], i / teamSize);
                        }
                    }
                }
                if (__instance.inSteamLobby)
                {
                    SteamMatchmaking.SetLobbyData(new CSteamID(__instance.CurrentLobbyID), "gamemode", gamemodeString);
                }
            }
            return false;
        }

        [HarmonyPatch(typeof(SteamLobby), "OnGetLobbyList")]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> OnGetLobbyListTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Ldc_I4_4 && (codes[i + 1].opcode == OpCodes.Ble || codes[i + 1].opcode == OpCodes.Ble_Un))
                {
                    codes[i] = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(HarmonyPatches), nameof(GetMaxPlayersConfigValue)));
                    break;
                }
            }
            return codes.AsEnumerable();
        }
        public static int GetMaxPlayersConfigValue() => SixPlayerPlugin.MaxPlayersConfig.Value;

        [HarmonyPatch(typeof(LobbyController), "Awake")]
        [HarmonyPostfix]
        public static void LobbyControllerAwakePostfix(LobbyController __instance)
        {
            int requiredClientSize = SixPlayerPlugin.MaxPlayersConfig.Value - 1;
            if (__instance.clientPosition != null && __instance.clientPosition.Length < requiredClientSize)
                __instance.clientPosition = ResizeAndRepositionTransforms(__instance.clientPosition, requiredClientSize, __instance.PlayerListViewContent.transform);
            if (__instance.tabclientPosition != null && __instance.tabclientPosition.Length < requiredClientSize)
            {
                Transform tabParent = __instance.tabclientPosition.Length > 0 ? __instance.tabclientPosition[0].parent : null;
                if (tabParent != null)
                    __instance.tabclientPosition = ResizeAndRepositionTransforms(__instance.tabclientPosition, requiredClientSize, tabParent);
            }
        }

        private static Transform[] ResizeAndRepositionTransforms(Transform[] originalArray, int newSize, Transform parent)
        {
            if (originalArray.Length >= newSize) return originalArray;
            Transform[] newArray = new Transform[newSize];
            Vector3 positionOffset = new Vector3(0, -55f, 0);
            if (originalArray.Length >= 2)
                positionOffset = originalArray[1].localPosition - originalArray[0].localPosition;
            System.Array.Copy(originalArray, newArray, originalArray.Length);
            Vector3 lastPosition = originalArray.Length > 0 ? originalArray[originalArray.Length - 1].localPosition : Vector3.zero;
            for (int i = originalArray.Length; i < newSize; i++)
            {
                GameObject newPosGo = new GameObject($"ClientPosition_{i + 1}");
                newPosGo.transform.SetParent(parent, false);
                newPosGo.transform.localPosition = lastPosition + (positionOffset * (i - originalArray.Length + 1));
                newArray[i] = newPosGo.transform;
            }
            return newArray;
        }

        [HarmonyPatch(typeof(MatchPoitnsHUD), "Start")]
        [HarmonyPostfix]
        public static void MatchPointsHUDStartPostfix(MatchPoitnsHUD __instance)
        {
            if (hudResized) return;
            int maxTeams = SixPlayerPlugin.MaxPlayersConfig.Value;
            var secondaryPointsField = AccessTools.Field(typeof(MatchPoitnsHUD), "secondaryPointObjects");
            MeshRenderer[] originalSecondaryPoints = (MeshRenderer[])secondaryPointsField.GetValue(__instance);
            if (originalSecondaryPoints != null && originalSecondaryPoints.Length < (maxTeams - 2))
            {
                originalSecondaryPoints = ResizeUIArray(originalSecondaryPoints, maxTeams - 2, "SecondaryPoint");
                secondaryPointsField.SetValue(__instance, originalSecondaryPoints);
            }
            var pointsTextsField = AccessTools.Field(typeof(MatchPoitnsHUD), "pointsTexts");
            TMP_Text[] originalPointsTexts = (TMP_Text[])pointsTextsField.GetValue(__instance);
            if (originalPointsTexts != null && originalPointsTexts.Length < maxTeams)
            {
                originalPointsTexts = ResizeUIArray(originalPointsTexts, maxTeams, "PointsText");
                pointsTextsField.SetValue(__instance, originalPointsTexts);
            }
            hudResized = true;
        }

        private static T[] ResizeUIArray<T>(T[] originalArray, int newSize, string namePrefix) where T : Component
        {
            if (originalArray.Length == 0) return new T[newSize];
            if (originalArray.Length >= newSize) return originalArray;
            List<T> list = new List<T>(originalArray);
            T template = list.Last();
            Transform parent = template.transform.parent;
            Vector3 offset = (list.Count >= 2) ? list[list.Count - 1].transform.localPosition - list[list.Count - 2].transform.localPosition : new Vector3(2.5f, 0, 0);
            for (int i = list.Count; i < newSize; i++)
            {
                GameObject newObj = UnityEngine.Object.Instantiate(template.gameObject, parent);
                newObj.name = $"{namePrefix}_{i}";
                newObj.transform.localPosition = template.transform.localPosition + (offset * (i - (list.Count - 1)));
                list.Add(newObj.GetComponent<T>());
            }
            return list.ToArray();
        }

        [HarmonyPatch(typeof(PlayerManager), "SetActiveSpawnPoints")]
        [HarmonyPostfix]
        public static void SetActiveSpawnPointsPostfix(PlayerManager __instance)
        {
            int playerCount = SteamLobby.Instance.players.Count;
            if (playerCount <= 4) return;
            var spawnPoint4v4Field = AccessTools.Field(typeof(PlayerManager), "SpawnPoint4v4");
            var currentSpawnPointsField = AccessTools.Field(typeof(PlayerManager), "CurrentSpawnPoints");
            SpawnPoint[] spawnPoints4v4 = (SpawnPoint[])spawnPoint4v4Field.GetValue(__instance);
            if (spawnPoints4v4 == null || spawnPoints4v4.Length == 0) return;
            if (spawnPoints4v4.Length < playerCount)
            {
                List<SpawnPoint> newSpawnPoints = new List<SpawnPoint>(spawnPoints4v4);
                SpawnPoint template = spawnPoints4v4.Last();
                Transform parent = template.transform.parent;
                for (int i = spawnPoints4v4.Length; i < playerCount; i++)
                {
                    GameObject newSpawnObj = UnityEngine.Object.Instantiate(template.gameObject, parent);
                    newSpawnObj.name = $"ModdedSpawnPoint_{i}";
                    newSpawnObj.transform.position += new Vector3(i * 1.5f, 0, 0);
                    newSpawnPoints.Add(newSpawnObj.GetComponent<SpawnPoint>());
                }
                spawnPoint4v4Field.SetValue(__instance, newSpawnPoints.ToArray());
                currentSpawnPointsField.SetValue(__instance, newSpawnPoints.ToArray());
            }
        }
    }
}
