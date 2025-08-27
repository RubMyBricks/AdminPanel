using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System;
using Network;
using Newtonsoft.Json;
using Rust;
using Oxide.Core;

namespace Oxide.Plugins
{
    [Info("AdminPanel", "RubMyBricks", "1.0.0")]
    public class AdminPanel : RustPlugin
    {
        private const string PanelName = "AdminPanel_UI";
        private const string SidebarName = "AdminPanel_Sidebar";
        private const string ContentName = "AdminPanel_Content";
        private const string DashboardName = "AdminPanel_Dashboard";
        private const string PlayersTabName = "AdminPanel_Players";
        private const string ReportsTabName = "AdminPanel_Reports";

        [PluginReference]
        private Plugin InventoryViewer;

        private Timer statsTimer;
        private bool statsLoaded = false;

        private ConfigData configData;
        private class ConfigData
        {
            public Dictionary<string, string> AllowedPlayers = new Dictionary<string, string>();
            public UIConfig UI = new UIConfig();
            public List<PlayerReport> StoredReports = new List<PlayerReport>();
        }

        private class UIConfig
        {
            public string PrimaryColor = "0.12 0.23 0.54 1";
            public string SecondaryColor = "0.23 0.51 0.96 1";
            public string DangerColor = "0.94 0.27 0.27 1";
            public string SuccessColor = "0.06 0.73 0.51 1";
            public string WarningColor = "0.96 0.62 0.04 1";
            public string BackgroundColor = "0.07 0.09 0.15 0.95";
            public string PanelColor = "0.12 0.16 0.24 0.9";
            public string TextColor = "0.9 0.92 0.93 1";
            public string SubtitleColor = "0.61 0.64 0.69 1";
            public int FontSize = 14;
            public string FontName = "robotocondensed-regular.ttf";
        }

        private Dictionary<ulong, bool> adminPanelStates = new Dictionary<ulong, bool>();
        private Dictionary<ulong, string> currentPanel = new Dictionary<ulong, string>();
        private Dictionary<ulong, int> playerListPage = new Dictionary<ulong, int>();
        private Dictionary<ulong, int> reportsPage = new Dictionary<ulong, int>();
        private Dictionary<ulong, bool> godModeStates = new Dictionary<ulong, bool>();
        private Dictionary<ulong, bool> vanishStates = new Dictionary<ulong, bool>();
        private Dictionary<ulong, bool> entityKillModeStates = new Dictionary<ulong, bool>();
        private const int PlayersPerPage = 10;
        private const int ReportsPerPage = 15;

        private int nextReportId = 1;

        // Permission
        private const string PermissionUse = "adminpanel.use";
        private const string PermissionAll = "adminpanel.all";
        private const string PermissionGodmode = "adminpanel.godmode";
        private const string PermissionVanish = "adminpanel.vanish";
        private const string PermissionTeleport = "adminpanel.teleport";
        private const string PermissionInventoryViewer = "adminpanel.inventoryviewer";
        private const string PermissionNoclip = "adminpanel.noclip";
        private const string PermissionSpectate = "adminpanel.spectate";
        private const string PermissionFeed = "adminpanel.feed";
        private const string PermissionMaxHealth = "adminpanel.maxhealth";
        private const string PermissionKick = "adminpanel.kick";
        private const string PermissionReports = "adminpanel.reports";
        private const string PermissionEntityKill = "adminpanel.entitykill";

        void Init()
        {
            permission.RegisterPermission(PermissionUse, this);
            permission.RegisterPermission(PermissionAll, this);
            permission.RegisterPermission(PermissionGodmode, this);
            permission.RegisterPermission(PermissionVanish, this);
            permission.RegisterPermission(PermissionTeleport, this);
            permission.RegisterPermission(PermissionInventoryViewer, this);
            permission.RegisterPermission(PermissionNoclip, this);
            permission.RegisterPermission(PermissionSpectate, this);
            permission.RegisterPermission(PermissionFeed, this);
            permission.RegisterPermission(PermissionMaxHealth, this);
            permission.RegisterPermission(PermissionKick, this);
            permission.RegisterPermission(PermissionReports, this);
            permission.RegisterPermission(PermissionEntityKill, this);
            LoadConfig();
            ConsoleSystem.Run(ConsoleSystem.Option.Server, "server.printReportsToConsole", true);
        }

        void OnPlayerReported(BasePlayer reporter, string targetName, string targetId, string subject, string message, string type)
        {
            if (reporter == null) return;

            try
            {
                var report = new PlayerReport
                {
                    reportId = (uint)nextReportId++,
                    reporterName = reporter.displayName ?? "Unknown",
                    targetName = targetName ?? "Unknown",
                    targetId = targetId ?? "Unknown",
                    type = type ?? "Other",
                    subject = subject ?? "No subject",
                    message = message ?? "No message",
                    timestamp = DateTime.Now
                };

                configData.StoredReports.Add(report);

                if (configData.StoredReports.Count > 100)
                {
                    configData.StoredReports.RemoveAt(0);
                }

                SaveConfig();

                PrintToConsole($"[AdminPanel] Report #{report.reportId}: {reporter.displayName} reported {targetName} for {type} - {subject}");

                foreach (var player in BasePlayer.activePlayerList)
                {
                    if (player != null && adminPanelStates.ContainsKey(player.userID) &&
                        adminPanelStates[player.userID] &&
                        currentPanel.ContainsKey(player.userID) &&
                        currentPanel[player.userID] == ReportsTabName)
                    {
                        timer.Once(0.1f, () => {
                            if (player != null && player.IsConnected)
                            {
                                DestroyUI(player);
                                CreateUI(player);
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                PrintError($"Error in OnPlayerReported: {ex.Message}");
            }
        }

        private void ShowReportDetails(BasePlayer player, uint reportId)
        {
            if (player == null) return;

            var report = configData.StoredReports.FirstOrDefault(r => r.reportId == reportId);
            if (report == null)
            {
                PrintToChat(player, "Report not found.");
                return;
            }

            PrintToChat(player, $"<color=#ffa500>--- Report #{report.reportId} Details ---</color>");
            PrintToChat(player, $"<color=#87ceeb>Reporter:</color> {report.reporterName}");
            PrintToChat(player, $"<color=#87ceeb>Target:</color> {report.targetName} (ID: {report.targetId})");
            PrintToChat(player, $"<color=#87ceeb>Type:</color> {report.type}");
            PrintToChat(player, $"<color=#87ceeb>Subject:</color> {report.subject}");
            PrintToChat(player, $"<color=#87ceeb>Message:</color> {report.message}");
            PrintToChat(player, $"<color=#87ceeb>Time:</color> {report.timestamp:yyyy-MM-dd HH:mm:ss}");
            PrintToChat(player, $"<color=#ffa500>--- End Report Details ---</color>");
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                configData = Config.ReadObject<ConfigData>();
                if (configData == null)
                {
                    LoadDefaultConfig();
                }

                if (configData.UI == null)
                    configData.UI = new UIConfig();

                if (configData.StoredReports == null)
                    configData.StoredReports = new List<PlayerReport>();

                // Set next report ID based on existing reports
                if (configData.StoredReports.Count > 0)
                {
                    nextReportId = (int)(configData.StoredReports.Max(r => r.reportId) + 1);
                }
            }
            catch
            {
                LoadDefaultConfig();
            }
            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            PrintWarning("Creating new configuration file");
            configData = new ConfigData();
        }

        protected override void SaveConfig()
        {
            if (configData == null)
            {
                LoadDefaultConfig();
            }
            Config.WriteObject(configData, true);
        }

        private bool IsPlayerAllowed(BasePlayer player)
        {
            if (player == null) return false;

            if (configData.AllowedPlayers.ContainsKey(player.UserIDString))
                return true;

            return permission.UserHasPermission(player.UserIDString, PermissionUse) ||
                   permission.UserHasPermission(player.UserIDString, PermissionAll);
        }

        private bool HasPermission(BasePlayer player, string perm)
        {
            if (player == null) return false;
            return permission.UserHasPermission(player.UserIDString, PermissionAll) ||
                   permission.UserHasPermission(player.UserIDString, perm);
        }

        void OnServerInitialized()
        {
            if (configData == null)
            {
                LoadDefaultConfig();
                SaveConfig();
            }

            try
            {
                foreach (var player in BasePlayer.activePlayerList.Where(p => p != null))
                {
                    adminPanelStates[player.userID] = false;
                    currentPanel[player.userID] = DashboardName;
                    playerListPage[player.userID] = 0;
                    reportsPage[player.userID] = 0;
                }

                // Give time for server to fully initialize before marking stats as loaded
                timer.Once(5f, () => {
                    statsLoaded = true;
                });
            }
            catch (System.Exception ex)
            {
                PrintError($"Error in OnServerInitialized: {ex.Message}");
            }
        }

        void OnPlayerConnected(BasePlayer player)
        {
            if (player == null) return;

            adminPanelStates[player.userID] = false;
            currentPanel[player.userID] = DashboardName;
            playerListPage[player.userID] = 0;
            reportsPage[player.userID] = 0;
        }

        void OnPlayerDisconnected(BasePlayer player)
        {
            if (player == null) return;

            if (vanishStates.ContainsKey(player.userID) && vanishStates[player.userID])
            {
                DisableVanish(player);
            }

            if (godModeStates.ContainsKey(player.userID) && godModeStates[player.userID])
            {
                DisableGodmode(player);
            }

            adminPanelStates.Remove(player.userID);
            currentPanel.Remove(player.userID);
            playerListPage.Remove(player.userID);
            reportsPage.Remove(player.userID);
            godModeStates.Remove(player.userID);
            vanishStates.Remove(player.userID);
            entityKillModeStates.Remove(player.userID);

            DestroyUI(player);
        }

        void Unload()
        {
            if (statsTimer != null)
                statsTimer.Destroy();

            foreach (var playerId in vanishStates.Keys.ToList())
            {
                var player = BasePlayer.FindByID(playerId);
                if (player != null)
                    DisableVanish(player);
            }

            foreach (var player in BasePlayer.activePlayerList.Where(p => p != null))
            {
                DestroyUI(player);
            }
        }

        private void ToggleGodmode(BasePlayer player)
        {
            if (player == null) return;

            if (!godModeStates.ContainsKey(player.userID))
                godModeStates[player.userID] = false;

            godModeStates[player.userID] = !godModeStates[player.userID];

            if (godModeStates[player.userID])
            {
                EnableGodmode(player);
            }
            else
            {
                DisableGodmode(player);
            }

            // Only update the specific button instead of recreating entire UI
            if (adminPanelStates.ContainsKey(player.userID) && adminPanelStates[player.userID])
            {
                UpdateGodmodeButton(player);
            }

            player.SendNetworkUpdate();
        }

        private void ToggleVanish(BasePlayer player)
        {
            if (player == null) return;

            if (!vanishStates.ContainsKey(player.userID))
                vanishStates[player.userID] = false;

            vanishStates[player.userID] = !vanishStates[player.userID];

            if (vanishStates[player.userID])
            {
                EnableVanish(player);
            }
            else
            {
                DisableVanish(player);
            }

            if (adminPanelStates.ContainsKey(player.userID) && adminPanelStates[player.userID])
            {
                UpdateVanishButton(player);
            }
        }

        private void UpdateGodmodeButton(BasePlayer player)
        {
            if (player == null) return;

            bool isGodmodeActive = godModeStates.ContainsKey(player.userID) && godModeStates[player.userID];
            string buttonText = isGodmodeActive ? "God Mode (ON)" : "God Mode (OFF)";
            string buttonColor = isGodmodeActive ? configData.UI.SuccessColor : configData.UI.SecondaryColor;

            CuiHelper.DestroyUi(player, "Btn_1_0_0"); 

            var elements = new CuiElementContainer();
            elements.Add(new CuiPanel
            {
                Image = { Color = buttonColor },
                RectTransform = { AnchorMin = "0 0.45", AnchorMax = "0.48 0.9" }
            }, "CmdButtons_1_0", "Btn_1_0_0");

            elements.Add(new CuiLabel
            {
                Text = {
                    Text = buttonText,
                    FontSize = 14,
                    Align = TextAnchor.MiddleCenter,
                    Color = configData.UI.TextColor,
                    Font = configData.UI.FontName
                },
                RectTransform = { AnchorMin = "0.05 0.05", AnchorMax = "0.95 0.95" }
            }, "Btn_1_0_0");

            elements.Add(new CuiButton
            {
                Button = {
                    Command = "adminpanel.execute godmode",
                    Color = "0 0 0 0"
                },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                Text = { Text = "" }
            }, "Btn_1_0_0");

            CuiHelper.AddUi(player, elements);
        }

        private void UpdateEntityKillButton(BasePlayer player)
        {
            if (player == null) return;

            bool isEntityKillActive = entityKillModeStates.ContainsKey(player.userID) && entityKillModeStates[player.userID];
            string buttonText = isEntityKillActive ? "Entity Kill (ON)" : "Entity Kill (OFF)";
            string buttonColor = isEntityKillActive ? configData.UI.WarningColor : configData.UI.SecondaryColor;

            CuiHelper.DestroyUi(player, "Btn_0_1_0"); // Entity Kill button position

            var elements = new CuiElementContainer();
            elements.Add(new CuiPanel
            {
                Image = { Color = buttonColor },
                RectTransform = { AnchorMin = "0 0.45", AnchorMax = "0.48 0.9" }
            }, "CmdButtons_0_1", "Btn_0_1_0");

            elements.Add(new CuiLabel
            {
                Text = {
                    Text = buttonText,
                    FontSize = 12,
                    Align = TextAnchor.MiddleCenter,
                    Color = configData.UI.TextColor,
                    Font = configData.UI.FontName
                },
                RectTransform = { AnchorMin = "0.05 0.05", AnchorMax = "0.95 0.95" }
            }, "Btn_0_1_0");

            elements.Add(new CuiButton
            {
                Button = {
                    Command = "adminpanel.execute entitykill",
                    Color = "0 0 0 0"
                },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                Text = { Text = "" }
            }, "Btn_0_1_0");

            CuiHelper.AddUi(player, elements);
        }

        private void UpdateVanishButton(BasePlayer player)
        {
            if (player == null) return;

            bool isVanishActive = vanishStates.ContainsKey(player.userID) && vanishStates[player.userID];
            string buttonText = isVanishActive ? "Vanish (ON)" : "Vanish (OFF)";
            string buttonColor = isVanishActive ? configData.UI.SuccessColor : configData.UI.SecondaryColor;

            CuiHelper.DestroyUi(player, "Btn_1_0_1"); // Vanish button position

            var elements = new CuiElementContainer();
            elements.Add(new CuiPanel
            {
                Image = { Color = buttonColor },
                RectTransform = { AnchorMin = "0.52 0.45", AnchorMax = "1 0.9" }
            }, "CmdButtons_1_0", "Btn_1_0_1");

            elements.Add(new CuiLabel
            {
                Text = {
                    Text = buttonText,
                    FontSize = 14,
                    Align = TextAnchor.MiddleCenter,
                    Color = configData.UI.TextColor,
                    Font = configData.UI.FontName
                },
                RectTransform = { AnchorMin = "0.05 0.05", AnchorMax = "0.95 0.95" }
            }, "Btn_1_0_1");

            elements.Add(new CuiButton
            {
                Button = {
                    Command = "adminpanel.execute vanish",
                    Color = "0 0 0 0"
                },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                Text = { Text = "" }
            }, "Btn_1_0_1");

            CuiHelper.AddUi(player, elements);
        }

        object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || info == null) return null;
            if (!(entity is BasePlayer)) return null;

            var player = entity as BasePlayer;
            if (godModeStates.ContainsKey(player.userID) && godModeStates[player.userID])
            {
                return true;
            }

            return null;
        }

        object OnPlayerViolation(BasePlayer player, AntiHackType type, float amount)
        {
            if (type == AntiHackType.FlyHack && godModeStates.ContainsKey(player.userID) && godModeStates[player.userID])
                return false;
            return null;
        }

        private void EnableGodmode(BasePlayer player)
        {
            if (player == null || !player.IsConnected) return;

            try
            {
                // Simple godmode - just set full protection
                player.health = 100f;

                // Set metabolism to safe values without protection properties
                if (player.metabolism != null)
                {
                    player.metabolism.calories.value = 500;
                    player.metabolism.hydration.value = 250;
                    player.metabolism.temperature.value = 20;
                    player.metabolism.radiation_poison.value = 0;
                    player.metabolism.oxygen.value = 1;
                    player.metabolism.wetness.value = 0;
                    player.metabolism.SendChangesToClient();
                }

                PrintToChat(player, "<color=#10B981>God Mode enabled</color>");
            }
            catch (System.Exception ex)
            {
                PrintError($"Error enabling godmode for {player.displayName}: {ex.Message}");
            }
        }

        private void DisableGodmode(BasePlayer player)
        {
            if (player == null || !player.IsConnected) return;

            try
            {
                // Reset metabolism to normal values
                if (player.metabolism != null)
                {
                    player.metabolism.calories.value = 250;
                    player.metabolism.hydration.value = 125;
                    player.metabolism.SendChangesToClient();
                }

                PrintToChat(player, "<color=#EF4444>God Mode disabled</color>");
            }
            catch (System.Exception ex)
            {
                PrintError($"Error disabling godmode for {player.displayName}: {ex.Message}");
            }
        }

        private void EnableVanish(BasePlayer player)
        {
            if (player == null || !player.IsConnected) return;

            try
            {
                player.syncPosition = false;
                player.limitNetworking = true;
                player._limitedNetworking = true;
                player.DisablePlayerCollider();

                var connections = new List<Connection>();
                foreach (var target in BasePlayer.activePlayerList)
                {
                    if (target != null && target != player && target.IsConnected && target.Connection != null)
                        connections.Add(target.Connection);
                }

                if (connections.Count > 0)
                {
                    NextTick(() => {
                        if (player != null && player.IsConnected)
                        {
                            player.OnNetworkSubscribersLeave(connections);
                        }
                    });
                }

                NextTick(() => {
                    if (player != null && player.IsConnected)
                    {
                        player.SendNetworkUpdate();
                    }
                });

                PrintToChat(player, "<color=#10B981>Vanish enabled</color> - You are now invisible to other players");
            }
            catch (System.Exception ex)
            {
                PrintError($"Error enabling vanish for {player.displayName}: {ex.Message}");
            }
        }

        private void DisableVanish(BasePlayer player)
        {
            if (player == null || !player.IsConnected) return;

            try
            {
                player.syncPosition = true;
                player.limitNetworking = false;
                player._limitedNetworking = false;
                player.EnablePlayerCollider();

                var connections = new List<Connection>();
                foreach (var target in BasePlayer.activePlayerList)
                {
                    if (target != null && target != player && target.IsConnected && target.Connection != null)
                        connections.Add(target.Connection);
                }

                if (connections.Count > 0)
                {
                    NextTick(() => {
                        if (player != null && player.IsConnected)
                        {
                            player.OnNetworkSubscribersEnter(connections);
                        }
                    });
                }

                timer.Once(0.1f, () => {
                    if (player != null && player.IsConnected)
                    {
                        player.SendNetworkUpdate();
                        player.SendFullSnapshot();
                    }
                });

                PrintToChat(player, "<color=#EF4444>Vanish disabled</color> - You are now visible to other players");
            }
            catch (System.Exception ex)
            {
                PrintError($"Error disabling vanish for {player.displayName}: {ex.Message}");
            }
        }

        #region UI Creation Methods

        private void CreateUI(BasePlayer player)
        {
            if (player == null) return;

            try
            {
                var elements = new CuiElementContainer();

                elements.Add(new CuiPanel
                {
                    Image = { Color = configData.UI.BackgroundColor, Material = "assets/content/ui/uibackgroundblur.mat" },
                    RectTransform = { AnchorMin = "0.1 0.1", AnchorMax = "0.9 0.9" },
                    CursorEnabled = true
                }, "Overlay", PanelName);

                CreateSidebar(elements, player);

                elements.Add(new CuiPanel
                {
                    Image = { Color = configData.UI.PanelColor },
                    RectTransform = { AnchorMin = "0.2 0", AnchorMax = "1 1" }
                }, PanelName, ContentName);

                elements.Add(new CuiPanel
                {
                    Image = { Color = $"0 0 0 0.3" },
                    RectTransform = { AnchorMin = "0 0.95", AnchorMax = "1 1" }
                }, ContentName, "TopInfoBar");

                if (statsLoaded)
                {
                    float fps = Performance.current.frameRate;
                    int players = BasePlayer.activePlayerList.Count;
                    string serverInfo = $"FPS: {fps:F1} | Players: {players}/{ConVar.Server.maxplayers} | {DateTime.Now:HH:mm:ss}";

                    elements.Add(new CuiLabel
                    {
                        Text = {
                            Text = serverInfo,
                            FontSize = 12,
                            Align = TextAnchor.MiddleLeft,
                            Color = configData.UI.SubtitleColor,
                            Font = configData.UI.FontName
                        },
                        RectTransform = { AnchorMin = "0.05 0", AnchorMax = "0.7 1" }
                    }, "TopInfoBar");
                }
                else
                {
                    elements.Add(new CuiLabel
                    {
                        Text = {
                            Text = "Loading server stats... | " + DateTime.Now.ToString("HH:mm:ss"),
                            FontSize = 12,
                            Align = TextAnchor.MiddleLeft,
                            Color = configData.UI.SubtitleColor,
                            Font = configData.UI.FontName
                        },
                        RectTransform = { AnchorMin = "0.05 0", AnchorMax = "0.7 1" }
                    }, "TopInfoBar");
                }

                string currentPanelName = currentPanel.ContainsKey(player.userID) ? currentPanel[player.userID] : DashboardName;

                switch (currentPanelName)
                {
                    case DashboardName:
                        CreateDashboard(elements, player);
                        break;
                    case PlayersTabName:
                        CreatePlayersTab(elements, player);
                        break;
                    case ReportsTabName:
                        CreateReportsTab(elements, player);
                        break;
                }

                elements.Add(new CuiButton
                {
                    Button = {
                        Command = "adminpanel.close",
                        Color = configData.UI.DangerColor
                    },
                    RectTransform = {
                        AnchorMin = "0.97 0.96",
                        AnchorMax = "0.99 0.99"
                    },
                    Text = {
                        Text = "✕",
                        FontSize = 16,
                        Align = TextAnchor.MiddleCenter,
                        Font = configData.UI.FontName
                    }
                }, PanelName);

                elements.Add(new CuiLabel
                {
                    Text = {
                        Text = "AdminPanel v2.1.0 by RubMyBricks",
                        FontSize = 10,
                        Align = TextAnchor.MiddleRight,
                        Color = "0.5 0.5 0.5 0.5",
                        Font = configData.UI.FontName
                    },
                    RectTransform = { AnchorMin = "0.7 0.01", AnchorMax = "0.99 0.03" }
                }, PanelName);

                CuiHelper.AddUi(player, elements);
            }
            catch (System.Exception ex)
            {
                PrintError($"Error creating UI: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void CreateSidebar(CuiElementContainer elements, BasePlayer player)
        {
            if (elements == null || player == null) return;

            elements.Add(new CuiPanel
            {
                Image = { Color = $"0 0 0 0.5" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0.2 1" }
            }, PanelName, SidebarName);

            elements.Add(new CuiLabel
            {
                Text = {
                    Text = "ADMIN",
                    FontSize = 20,
                    Align = TextAnchor.MiddleCenter,
                    Color = configData.UI.TextColor,
                    Font = "robotocondensed-bold.ttf"
                },
                RectTransform = { AnchorMin = "0 0.93", AnchorMax = "1 1" }
            }, SidebarName);

            elements.Add(new CuiLabel
            {
                Text = {
                    Text = "PANEL",
                    FontSize = 20,
                    Align = TextAnchor.MiddleCenter,
                    Color = configData.UI.SecondaryColor,
                    Font = "robotocondensed-bold.ttf"
                },
                RectTransform = { AnchorMin = "0 0.88", AnchorMax = "1 0.95" }
            }, SidebarName);

            string currentPanelName = currentPanel.ContainsKey(player.userID) ? currentPanel[player.userID] : DashboardName;

            AddNavItem(elements, DashboardName, "Dashboard", "", 0.8f, currentPanelName == DashboardName);
            AddNavItem(elements, PlayersTabName, "Players", "", 0.7f, currentPanelName == PlayersTabName);
            AddNavItem(elements, ReportsTabName, "Reports", "", 0.6f, currentPanelName == ReportsTabName);

            elements.Add(new CuiLabel
            {
                Text = {
                    Text = $"{ConVar.Server.hostname}",
                    FontSize = 12,
                    Align = TextAnchor.MiddleCenter,
                    Color = configData.UI.SubtitleColor,
                    Font = configData.UI.FontName
                },
                RectTransform = { AnchorMin = "0.05 0.02", AnchorMax = "0.95 0.05" }
            }, SidebarName);
        }

        private void AddNavItem(CuiElementContainer elements, string panelName, string label, string iconName, float yPos, bool selected = false)
        {
            string color = selected ? configData.UI.PrimaryColor : "0 0 0 0";
            string textColor = selected ? configData.UI.TextColor : configData.UI.SubtitleColor;

            elements.Add(new CuiPanel
            {
                Image = { Color = color },
                RectTransform = { AnchorMin = $"0.1 {yPos - 0.08}", AnchorMax = $"0.9 {yPos}" }
            }, SidebarName, $"Nav_{panelName}");

            elements.Add(new CuiLabel
            {
                Text = {
                    Text = label,
                    FontSize = 14,
                    Align = TextAnchor.MiddleCenter,
                    Color = textColor,
                    Font = configData.UI.FontName
                },
                RectTransform = { AnchorMin = "0.05 0", AnchorMax = "0.95 1" }
            }, $"Nav_{panelName}");

            elements.Add(new CuiButton
            {
                Button = {
                    Command = $"adminpanel.execute switchpanel {panelName}",
                    Color = "0 0 0 0"
                },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                Text = { Text = "" }
            }, $"Nav_{panelName}");
        }

        private void CreateDashboard(CuiElementContainer elements, BasePlayer player)
        {
            if (elements == null || player == null) return;

            elements.Add(new CuiLabel
            {
                Text = {
                    Text = "Dashboard",
                    FontSize = 24,
                    Align = TextAnchor.MiddleLeft,
                    Color = configData.UI.TextColor,
                    Font = "robotocondensed-bold.ttf"
                },
                RectTransform = { AnchorMin = "0.02 0.9", AnchorMax = "0.5 0.95" }
            }, ContentName);

            elements.Add(new CuiLabel
            {
                Text = {
                    Text = "Server administration tools",
                    FontSize = 14,
                    Align = TextAnchor.MiddleLeft,
                    Color = configData.UI.SubtitleColor,
                    Font = configData.UI.FontName
                },
                RectTransform = { AnchorMin = "0.02 0.87", AnchorMax = "0.5 0.9" }
            }, ContentName);

            CreateStatsRow(elements);
            StartStatsTimer(player);
            CreateCommandGroups(elements, player);
        }

        private void CreateStatsRow(CuiElementContainer elements)
        {
            if (elements == null) return;

            elements.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0.02 0.75", AnchorMax = "0.98 0.85" }
            }, ContentName, "StatsRow");

            if (statsLoaded)
            {
                int playersOnline = BasePlayer.activePlayerList.Count;
                float serverFps = Performance.current.frameRate;
                int entityCount = BaseNetworkable.serverEntities.Count;
                string currentTime = TOD_Sky.Instance != null ? $"{TOD_Sky.Instance.Cycle.Hour:F1}:00" : "12:00";

                CreateStatsCard(elements, "PLAYERS ONLINE", playersOnline.ToString(), "", 0, 4);
                CreateStatsCard(elements, "SERVER FPS", $"{serverFps:F1}", "", 1, 4);
                CreateStatsCard(elements, "ENTITIES", entityCount.ToString("N0"), "", 2, 4);
                CreateStatsCard(elements, "SERVER TIME", currentTime, "", 3, 4);
            }
            else
            {
                CreateStatsCard(elements, "PLAYERS ONLINE", "Loading...", "", 0, 4);
                CreateStatsCard(elements, "SERVER FPS", "Loading...", "", 1, 4);
                CreateStatsCard(elements, "ENTITIES", "Loading...", "", 2, 4);
                CreateStatsCard(elements, "SERVER TIME", "Loading...", "", 3, 4);
            }
        }

        private void CreateStatsCard(CuiElementContainer elements, string title, string value, string iconName, int position, int total)
        {
            float cardWidth = 0.98f / total;
            float spacing = 0.02f / (total - 1);
            float xMin = position * (cardWidth + spacing);
            float xMax = xMin + cardWidth;

            elements.Add(new CuiPanel
            {
                Image = { Color = configData.UI.PanelColor },
                RectTransform = { AnchorMin = $"{xMin} 0", AnchorMax = $"{xMax} 1" }
            }, "StatsRow", $"StatsCard_{position}");

            elements.Add(new CuiLabel
            {
                Text = {
                    Text = title,
                    FontSize = 12,
                    Align = TextAnchor.UpperCenter,
                    Color = configData.UI.SubtitleColor,
                    Font = configData.UI.FontName
                },
                RectTransform = { AnchorMin = "0.05 0.6", AnchorMax = "0.95 0.9" }
            }, $"StatsCard_{position}");

            elements.Add(new CuiLabel
            {
                Text = {
                    Text = value,
                    FontSize = 22,
                    Align = TextAnchor.MiddleCenter,
                    Color = configData.UI.TextColor,
                    Font = "robotocondensed-bold.ttf"
                },
                RectTransform = { AnchorMin = "0.05 0.2", AnchorMax = "0.95 0.6" }
            }, $"StatsCard_{position}");

            elements.Add(new CuiPanel
            {
                Image = { Color = configData.UI.PrimaryColor },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0.01 1" }
            }, $"StatsCard_{position}");
        }

        private void CreateCommandGroups(CuiElementContainer elements, BasePlayer player)
        {
            if (elements == null) return;

            elements.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0.02 0.05", AnchorMax = "0.98 0.73" }
            }, ContentName, "CommandGroups");

            CreateCommandGroup(elements, player, "Movement Controls", "", 0, 0, new Dictionary<string, string>
            {
                { "NoClip", "noclip" },
                { "Spectate", "spectate" },
                { "Stop Spectate", "stopspectate" },
                { "Teleport", "tp" }
            });

            CreateCommandGroup(elements, player, "Protection Controls", "", 1, 0, new Dictionary<string, string>
            {
                { "God Mode", "godmode" },
                { "Vanish", "vanish" },
                { "Max Health", "maxhealth" },
                { "Feed", "feed" }
            });

            CreateCommandGroup(elements, player, "Administration", "", 0, 1, new Dictionary<string, string>
            {
                { "Entity Kill Mode", "entitykill" }
            }, true);

            CreateCommandGroup(elements, player, "Time Controls", "", 1, 1, new Dictionary<string, string>
            {
                { "Set Day", "settime 12" },
                { "Set Night", "settime 0" },
                { "Set Dawn", "settime 6" },
                { "Set Dusk", "settime 18" }
            });
        }

        private void CreateCommandGroup(CuiElementContainer elements, BasePlayer player, string title, string iconName, int column, int row, Dictionary<string, string> commands, bool isDanger = false)
        {
            float groupWidth = 0.48f;
            float groupHeight = 0.48f;
            float xSpacing = 0.04f;
            float ySpacing = 0.04f;
            float xMin = column * (groupWidth + xSpacing);
            float yMin = row * (groupHeight + ySpacing);
            float xMax = xMin + groupWidth;
            float yMax = yMin + groupHeight;

            elements.Add(new CuiPanel
            {
                Image = { Color = configData.UI.PanelColor },
                RectTransform = { AnchorMin = $"{xMin} {yMin}", AnchorMax = $"{xMax} {yMax}" }
            }, "CommandGroups", $"CmdGroup_{column}_{row}");

            elements.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.3" },
                RectTransform = { AnchorMin = "0 0.85", AnchorMax = "1 1" }
            }, $"CmdGroup_{column}_{row}", $"CmdHeader_{column}_{row}");

            elements.Add(new CuiLabel
            {
                Text = {
                    Text = title,
                    FontSize = 16,
                    Align = TextAnchor.MiddleCenter,
                    Color = configData.UI.TextColor,
                    Font = "robotocondensed-bold.ttf"
                },
                RectTransform = { AnchorMin = "0.05 0", AnchorMax = "0.95 1" }
            }, $"CmdHeader_{column}_{row}");

            elements.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0.05 0.05", AnchorMax = "0.95 0.8" }
            }, $"CmdGroup_{column}_{row}", $"CmdButtons_{column}_{row}");

            int index = 0;
            int buttonsPerRow = 2;
            float buttonHeight = 0.45f;
            float buttonWidth = 0.48f;
            float buttonXSpacing = 0.04f;
            float buttonYSpacing = 0.1f;

            foreach (var command in commands)
            {
                int buttonRow = index / buttonsPerRow;
                int buttonCol = index % buttonsPerRow;

                float btnXMin = buttonCol * (buttonWidth + buttonXSpacing);
                float btnYMin = 1 - (buttonRow + 1) * buttonHeight - buttonRow * buttonYSpacing;
                float btnXMax = btnXMin + buttonWidth;
                float btnYMax = btnYMin + buttonHeight;

                string buttonColor = isDanger && index > 0 ? configData.UI.DangerColor : configData.UI.SecondaryColor;
                string buttonText = command.Key;

                bool hasPermission = CheckCommandPermission(player, command.Value);
                if (!hasPermission)
                {
                    buttonColor = "0.3 0.3 0.3 0.5";
                }

                if (command.Key == "God Mode")
                {
                    bool isGodmodeActive = godModeStates.ContainsKey(player.userID) && godModeStates[player.userID];
                    if (hasPermission)
                    {
                        buttonColor = isGodmodeActive ? configData.UI.SuccessColor : configData.UI.SecondaryColor;
                        buttonText = isGodmodeActive ? "God Mode (ON)" : "God Mode (OFF)";
                    }
                }
                else if (command.Key == "Vanish")
                {
                    bool isVanishActive = vanishStates.ContainsKey(player.userID) && vanishStates[player.userID];
                    if (hasPermission)
                    {
                        buttonColor = isVanishActive ? configData.UI.SuccessColor : configData.UI.SecondaryColor;
                        buttonText = isVanishActive ? "Vanish (ON)" : "Vanish (OFF)";
                    }
                }
                else if (command.Key == "Entity Kill Mode")
                {
                    bool hasEntityKillPerm = CheckCommandPermission(player, command.Value);
                    if (!hasEntityKillPerm)
                    {
                        buttonColor = "0.3 0.3 0.3 0.5";
                    }
                    else
                    {
                        bool isEntityKillActive = entityKillModeStates.ContainsKey(player.userID) && entityKillModeStates[player.userID];
                        buttonColor = isEntityKillActive ? configData.UI.WarningColor : configData.UI.SecondaryColor;
                        buttonText = isEntityKillActive ? "Entity Kill (ON)" : "Entity Kill (OFF)";
                    }

                    elements.Add(new CuiPanel
                    {
                        Image = { Color = buttonColor },
                        RectTransform = { AnchorMin = $"{btnXMin} {btnYMin}", AnchorMax = $"{btnXMax} {btnYMax}" }
                    }, $"CmdButtons_{column}_{row}", $"Btn_{column}_{row}_{index}");

                    elements.Add(new CuiLabel
                    {
                        Text = {
                            Text = buttonText,
                            FontSize = 12,
                            Align = TextAnchor.MiddleCenter,
                            Color = hasPermission ? configData.UI.TextColor : "0.7 0.7 0.7 0.7",
                            Font = configData.UI.FontName
                        },
                        RectTransform = { AnchorMin = "0.05 0.05", AnchorMax = "0.95 0.95" }
                    }, $"Btn_{column}_{row}_{index}");

                    if (hasPermission)
                    {
                        elements.Add(new CuiButton
                        {
                            Button = {
                                Command = $"adminpanel.execute entitykill",
                                Color = "0 0 0 0"
                            },
                            RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                            Text = { Text = "" }
                        }, $"Btn_{column}_{row}_{index}");
                    }

                    index++;
                    continue;
                }

                if (!hasPermission)
                {
                    buttonText += " (No Permission)";
                }

                elements.Add(new CuiPanel
                {
                    Image = { Color = buttonColor },
                    RectTransform = { AnchorMin = $"{btnXMin} {btnYMin}", AnchorMax = $"{btnXMax} {btnYMax}" }
                }, $"CmdButtons_{column}_{row}", $"Btn_{column}_{row}_{index}");

                elements.Add(new CuiLabel
                {
                    Text = {
                        Text = buttonText,
                        FontSize = 14,
                        Align = TextAnchor.MiddleCenter,
                        Color = hasPermission ? configData.UI.TextColor : "0.7 0.7 0.7 0.7",
                        Font = configData.UI.FontName
                    },
                    RectTransform = { AnchorMin = "0.05 0.05", AnchorMax = "0.95 0.95" }
                }, $"Btn_{column}_{row}_{index}");

                if (hasPermission)
                {
                    string[] parts = command.Value.Split(' ');
                    string baseCommand = parts[0];
                    string args = parts.Length > 1 ? string.Join(" ", parts.Skip(1).ToArray()) : "";

                    elements.Add(new CuiButton
                    {
                        Button = {
                            Command = $"adminpanel.execute {baseCommand} {args}",
                            Color = "0 0 0 0"
                        },
                        RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                        Text = { Text = "" }
                    }, $"Btn_{column}_{row}_{index}");
                }

                index++;
            }
        }

        private bool CheckCommandPermission(BasePlayer player, string command)
        {
            if (HasPermission(player, PermissionAll)) return true;

            string baseCommand = command.Split(' ')[0];

            switch (baseCommand)
            {
                case "godmode":
                    return HasPermission(player, PermissionGodmode);
                case "vanish":
                    return HasPermission(player, PermissionVanish);
                case "noclip":
                    return HasPermission(player, PermissionNoclip);
                case "spectate":
                case "stopspectate":
                    return HasPermission(player, PermissionSpectate);
                case "tp":
                case "tpto":
                case "tphere":
                    return HasPermission(player, PermissionTeleport);
                case "feed":
                    return HasPermission(player, PermissionFeed);
                case "maxhealth":
                    return HasPermission(player, PermissionMaxHealth);
                case "kickplayer":
                    return HasPermission(player, PermissionKick);
                case "entitykill":
                    return HasPermission(player, PermissionEntityKill);
                case "settime":
                    return true; // Everyone can use set day/night
                default:
                    return true;
            }
        }

        private void CreatePlayersTab(CuiElementContainer elements, BasePlayer admin)
        {
            if (elements == null || admin == null) return;

            elements.Add(new CuiLabel
            {
                Text = {
                    Text = "Player Management",
                    FontSize = 24,
                    Align = TextAnchor.MiddleLeft,
                    Color = configData.UI.TextColor,
                    Font = "robotocondensed-bold.ttf"
                },
                RectTransform = { AnchorMin = "0.02 0.9", AnchorMax = "0.5 0.95" }
            }, ContentName);

            elements.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0.02 0.83", AnchorMax = "0.98 0.88" }
            }, ContentName, "PlayerSearchBar");

            elements.Add(new CuiPanel
            {
                Image = { Color = configData.UI.PanelColor },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0.3 1" }
            }, "PlayerSearchBar", "SearchInput");

            elements.Add(new CuiLabel
            {
                Text = {
                    Text = "🔍",
                    FontSize = 16,
                    Align = TextAnchor.MiddleCenter,
                    Color = configData.UI.SubtitleColor,
                    Font = configData.UI.FontName
                },
                RectTransform = { AnchorMin = "0.02 0", AnchorMax = "0.1 1" }
            }, "SearchInput");

            elements.Add(new CuiLabel
            {
                Text = {
                    Text = "Search players... (Coming in next update)",
                    FontSize = 14,
                    Align = TextAnchor.MiddleLeft,
                    Color = configData.UI.SubtitleColor,
                    Font = configData.UI.FontName
                },
                RectTransform = { AnchorMin = "0.12 0", AnchorMax = "0.95 1" }
            }, "SearchInput");

            elements.Add(new CuiButton
            {
                Button = {
                    Command = "adminpanel.execute refreshplayers",
                    Color = configData.UI.SuccessColor
                },
                RectTransform = { AnchorMin = "0.95 0", AnchorMax = "1 1" },
                Text = {
                    Text = "⟳",
                    FontSize = 18,
                    Align = TextAnchor.MiddleCenter,
                    Color = configData.UI.TextColor,
                    Font = configData.UI.FontName
                }
            }, "PlayerSearchBar");

            elements.Add(new CuiPanel
            {
                Image = { Color = configData.UI.PanelColor },
                RectTransform = { AnchorMin = "0.02 0.15", AnchorMax = "0.98 0.8" }
            }, ContentName, "PlayerListContainer");

            elements.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.3" },
                RectTransform = { AnchorMin = "0 0.95", AnchorMax = "1 1" }
            }, "PlayerListContainer", "PlayerListHeader");

            string[] columns = { "", "Player Name", "SteamID", "Ping", "Actions" };
            float[] columnWidths = { 0.05f, 0.35f, 0.2f, 0.1f, 0.3f };

            float currentX = 0;
            for (int i = 0; i < columns.Length; i++)
            {
                elements.Add(new CuiLabel
                {
                    Text = {
                        Text = columns[i],
                        FontSize = 14,
                        Align = TextAnchor.MiddleLeft,
                        Color = configData.UI.SubtitleColor,
                        Font = configData.UI.FontName
                    },
                    RectTransform = {
                        AnchorMin = $"{currentX + 0.01} 0",
                        AnchorMax = $"{currentX + columnWidths[i]} 1"
                    }
                }, "PlayerListHeader");

                currentX += columnWidths[i];
            }

            var players = BasePlayer.activePlayerList.OrderBy(p => p.displayName).ToList();
            int totalPlayers = players.Count;
            int totalPages = (totalPlayers - 1) / PlayersPerPage + 1;
            int currentPage = playerListPage.ContainsKey(admin.userID) ? playerListPage[admin.userID] : 0;

            if (currentPage >= totalPages)
                currentPage = totalPages - 1;
            if (currentPage < 0)
                currentPage = 0;

            playerListPage[admin.userID] = currentPage;

            int startIndex = currentPage * PlayersPerPage;
            float rowHeight = 0.9f / PlayersPerPage;

            elements.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 0.95" }
            }, "PlayerListContainer", "PlayerRows");

            for (int i = 0; i < PlayersPerPage && startIndex + i < totalPlayers; i++)
            {
                var player = players[startIndex + i];
                if (player == null) continue;

                float yMin = 1 - (i + 1) * rowHeight;
                float yMax = yMin + rowHeight;

                string rowColor = i % 2 == 0 ? "0.1 0.1 0.1 0.3" : "0.12 0.12 0.12 0.2";
                elements.Add(new CuiPanel
                {
                    Image = { Color = rowColor },
                    RectTransform = { AnchorMin = $"0 {yMin}", AnchorMax = $"1 {yMax}" }
                }, "PlayerRows", $"PlayerRow_{i}");

                elements.Add(new CuiPanel
                {
                    Image = { Color = configData.UI.PrimaryColor },
                    RectTransform = {
                        AnchorMin = $"0.015 {(yMax - yMin) * 0.25}",
                        AnchorMax = $"0.035 {(yMax - yMin) * 0.75}"
                    }
                }, $"PlayerRow_{i}");

                elements.Add(new CuiLabel
                {
                    Text = {
                        Text = player.displayName,
                        FontSize = 14,
                        Align = TextAnchor.MiddleLeft,
                        Color = configData.UI.TextColor,
                        Font = configData.UI.FontName
                    },
                    RectTransform = { AnchorMin = "0.06 0", AnchorMax = "0.4 1" }
                }, $"PlayerRow_{i}");

                elements.Add(new CuiLabel
                {
                    Text = {
                        Text = player.UserIDString,
                        FontSize = 14,
                        Align = TextAnchor.MiddleLeft,
                        Color = configData.UI.SubtitleColor,
                        Font = configData.UI.FontName
                    },
                    RectTransform = { AnchorMin = "0.41 0", AnchorMax = "0.6 1" }
                }, $"PlayerRow_{i}");

                int ping = Network.Net.sv.GetAveragePing(player.Connection);
                elements.Add(new CuiLabel
                {
                    Text = {
                        Text = $"{ping} ms",
                        FontSize = 14,
                        Align = TextAnchor.MiddleLeft,
                        Color = configData.UI.SubtitleColor,
                        Font = configData.UI.FontName
                    },
                    RectTransform = { AnchorMin = "0.61 0", AnchorMax = "0.7 1" }
                }, $"PlayerRow_{i}");

                elements.Add(new CuiPanel
                {
                    Image = { Color = "0 0 0 0" },
                    RectTransform = { AnchorMin = "0.71 0.1", AnchorMax = "0.99 0.9" }
                }, $"PlayerRow_{i}", $"PlayerActions_{i}");

                CreatePlayerActionButton(elements, admin, "T2P", $"tpto {player.userID}", 0, 5, $"PlayerActions_{i}", configData.UI.PrimaryColor);
                CreatePlayerActionButton(elements, admin, "T2M", $"tphere {player.userID}", 1, 5, $"PlayerActions_{i}", configData.UI.SecondaryColor);
                CreatePlayerActionButton(elements, admin, "Inv", $"viewinv {player.userID}", 2, 5, $"PlayerActions_{i}", configData.UI.SuccessColor);
                CreatePlayerActionButton(elements, admin, "Kick", $"kickplayer {player.userID}", 3, 5, $"PlayerActions_{i}", configData.UI.WarningColor);
                CreatePlayerActionButton(elements, admin, "Feed", $"feed {player.userID}", 4, 5, $"PlayerActions_{i}", configData.UI.SuccessColor);
            }

            elements.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0.02 0.05", AnchorMax = "0.98 0.1" }
            }, ContentName, "PaginationControls");

            elements.Add(new CuiLabel
            {
                Text = {
                    Text = $"Page {currentPage + 1} of {totalPages} ({totalPlayers} players)",
                    FontSize = 14,
                    Align = TextAnchor.MiddleCenter,
                    Color = configData.UI.SubtitleColor,
                    Font = configData.UI.FontName
                },
                RectTransform = { AnchorMin = "0.4 0", AnchorMax = "0.6 1" }
            }, "PaginationControls");

            if (currentPage > 0)
            {
                elements.Add(new CuiButton
                {
                    Button = {
                        Command = "adminpanel.execute prevpage",
                        Color = configData.UI.PrimaryColor
                    },
                    RectTransform = { AnchorMin = "0.3 0", AnchorMax = "0.38 1" },
                    Text = {
                        Text = "◄ Previous",
                        FontSize = 14,
                        Align = TextAnchor.MiddleCenter,
                        Color = configData.UI.TextColor,
                        Font = configData.UI.FontName
                    }
                }, "PaginationControls");
            }

            if (currentPage < totalPages - 1)
            {
                elements.Add(new CuiButton
                {
                    Button = {
                        Command = "adminpanel.execute nextpage",
                        Color = configData.UI.PrimaryColor
                    },
                    RectTransform = { AnchorMin = "0.62 0", AnchorMax = "0.7 1" },
                    Text = {
                        Text = "Next ►",
                        FontSize = 14,
                        Align = TextAnchor.MiddleCenter,
                        Color = configData.UI.TextColor,
                        Font = configData.UI.FontName
                    }
                }, "PaginationControls");
            }
        }

        private void CreatePlayerActionButton(CuiElementContainer elements, BasePlayer admin, string text, string command, int position, int total, string parent, string color)
        {
            float buttonWidth = 0.95f / total;
            float spacing = 0.05f / (total - 1);
            float xMin = position * (buttonWidth + spacing);
            float xMax = xMin + buttonWidth;

            string[] parts = command.Split(' ');
            string baseCommand = parts[0];
            string args = parts.Length > 1 ? string.Join(" ", parts.Skip(1).ToArray()) : "";
            bool hasPermission = CheckPlayerActionPermission(admin, baseCommand);

            if (!hasPermission)
            {
                color = "0.3 0.3 0.3 0.5";
                text += " (No Perm)";
            }

            elements.Add(new CuiButton
            {
                Button = {
                    Command = hasPermission ? $"adminpanel.execute {baseCommand} {args}" : "",
                    Color = color
                },
                RectTransform = { AnchorMin = $"{xMin} 0", AnchorMax = $"{xMax} 1" },
                Text = {
                    Text = text,
                    FontSize = 10,
                    Align = TextAnchor.MiddleCenter,
                    Color = hasPermission ? configData.UI.TextColor : "0.7 0.7 0.7 0.7",
                    Font = configData.UI.FontName
                }
            }, parent);
        }

        private bool CheckPlayerActionPermission(BasePlayer player, string command)
        {
            if (HasPermission(player, PermissionAll)) return true;

            switch (command)
            {
                case "tpto":
                case "tphere":
                    return HasPermission(player, PermissionTeleport);
                case "viewinv":
                    return HasPermission(player, PermissionInventoryViewer);
                case "kickplayer":
                    return HasPermission(player, PermissionKick);
                case "feed":
                    return HasPermission(player, PermissionFeed);
                default:
                    return true;
            }
        }

        private void CreateReportsTab(CuiElementContainer elements, BasePlayer player)
        {
            if (elements == null) return;

            elements.Add(new CuiLabel
            {
                Text = {
                    Text = "Player Reports (F7)",
                    FontSize = 24,
                    Align = TextAnchor.MiddleLeft,
                    Color = configData.UI.TextColor,
                    Font = "robotocondensed-bold.ttf"
                },
                RectTransform = { AnchorMin = "0.02 0.9", AnchorMax = "0.7 0.95" }
            }, ContentName);

            if (!HasPermission(player, PermissionReports))
            {
                elements.Add(new CuiLabel
                {
                    Text = {
                        Text = "You don't have permission to view player reports",
                        FontSize = 16,
                        Align = TextAnchor.MiddleCenter,
                        Color = configData.UI.DangerColor,
                        Font = configData.UI.FontName
                    },
                    RectTransform = { AnchorMin = "0.2 0.4", AnchorMax = "0.8 0.6" }
                }, ContentName);
                return;
            }

            elements.Add(new CuiButton
            {
                Button = {
                    Command = "adminpanel.execute refreshreports",
                    Color = configData.UI.SuccessColor
                },
                RectTransform = { AnchorMin = "0.85 0.9", AnchorMax = "0.98 0.95" },
                Text = {
                    Text = "⟳ Refresh",
                    FontSize = 14,
                    Align = TextAnchor.MiddleCenter,
                    Color = configData.UI.TextColor,
                    Font = configData.UI.FontName
                }
            }, ContentName);

            elements.Add(new CuiPanel
            {
                Image = { Color = configData.UI.PanelColor },
                RectTransform = { AnchorMin = "0.02 0.15", AnchorMax = "0.98 0.85" }
            }, ContentName, "ReportsContainer");

            elements.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.3" },
                RectTransform = { AnchorMin = "0 0.95", AnchorMax = "1 1" }
            }, "ReportsContainer", "ReportsHeader");

            string[] columns = { "ID", "Time", "Reporter", "Target", "Type", "Subject", "Actions" };
            float[] widths = { 0.06f, 0.12f, 0.16f, 0.16f, 0.12f, 0.26f, 0.12f };
            float currentX = 0f;

            for (int i = 0; i < columns.Length; i++)
            {
                elements.Add(new CuiLabel
                {
                    Text = {
                        Text = columns[i],
                        FontSize = 12,
                        Align = TextAnchor.MiddleLeft,
                        Color = configData.UI.SubtitleColor,
                        Font = configData.UI.FontName
                    },
                    RectTransform = {
                        AnchorMin = $"{currentX + 0.005} 0",
                        AnchorMax = $"{currentX + widths[i]} 1"
                    }
                }, "ReportsHeader");

                currentX += widths[i];
            }

            var allReports = GetPlayerReports();
            int totalReports = allReports.Count;
            int totalPages = Math.Max(1, (totalReports - 1) / ReportsPerPage + 1);
            int currentPage = reportsPage.ContainsKey(player.userID) ? reportsPage[player.userID] : 0;

            if (currentPage >= totalPages)
                currentPage = totalPages - 1;
            if (currentPage < 0)
                currentPage = 0;

            reportsPage[player.userID] = currentPage;

            elements.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0 0.1", AnchorMax = "1 0.95" }
            }, "ReportsContainer", "ReportsContent");

            if (totalReports == 0)
            {
                elements.Add(new CuiLabel
                {
                    Text = {
                        Text = "No player reports found\nReports will appear here when players use F7 to report issues",
                        FontSize = 16,
                        Align = TextAnchor.MiddleCenter,
                        Color = configData.UI.SubtitleColor,
                        Font = configData.UI.FontName
                    },
                    RectTransform = { AnchorMin = "0 0.4", AnchorMax = "1 0.6" }
                }, "ReportsContent");
            }
            else
            {
                int startIndex = currentPage * ReportsPerPage;
                int endIndex = Math.Min(startIndex + ReportsPerPage, totalReports);
                float rowHeight = 0.85f / ReportsPerPage;

                for (int i = 0; i < ReportsPerPage && startIndex + i < totalReports; i++)
                {
                    var report = allReports[startIndex + i];
                    float yMin = 1 - ((i + 1) * rowHeight);
                    float yMax = yMin + rowHeight;

                    string rowColor = i % 2 == 0 ? "0.1 0.1 0.1 0.2" : "0.12 0.12 0.12 0.15";
                    elements.Add(new CuiPanel
                    {
                        Image = { Color = rowColor },
                        RectTransform = { AnchorMin = $"0 {yMin}", AnchorMax = $"1 {yMax}" }
                    }, "ReportsContent", $"ReportRow_{i}");

                    currentX = 0f;

                    // Report ID
                    CreateReportCell(elements, report.reportId.ToString(), currentX, widths[0], $"ReportRow_{i}");
                    currentX += widths[0];

                    // Time
                    CreateReportCell(elements, report.timestamp.ToString("HH:mm\ndd/MM"), currentX, widths[1], $"ReportRow_{i}");
                    currentX += widths[1];

                    // Reporter
                    CreateReportCell(elements, TruncateText(report.reporterName, 15), currentX, widths[2], $"ReportRow_{i}");
                    currentX += widths[2];

                    // Target
                    CreateReportCell(elements, TruncateText(report.targetName, 15), currentX, widths[3], $"ReportRow_{i}");
                    currentX += widths[3];

                    // Type
                    CreateReportCell(elements, report.type, currentX, widths[4], $"ReportRow_{i}");
                    currentX += widths[4];

                    // Subject
                    CreateReportCell(elements, TruncateText(report.subject, 25), currentX, widths[5], $"ReportRow_{i}");
                    currentX += widths[5];

                    // Actions
                    CreateReportActionButtons(elements, report, currentX, widths[6], $"ReportRow_{i}");
                }

                elements.Add(new CuiPanel
                {
                    Image = { Color = "0 0 0 0" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 0.1" }
                }, "ReportsContainer", "ReportsPagination");

                elements.Add(new CuiLabel
                {
                    Text = {
                        Text = $"Page {currentPage + 1} of {totalPages} ({totalReports} reports)",
                        FontSize = 14,
                        Align = TextAnchor.MiddleCenter,
                        Color = configData.UI.SubtitleColor,
                        Font = configData.UI.FontName
                    },
                    RectTransform = { AnchorMin = "0.4 0.2", AnchorMax = "0.6 0.8" }
                }, "ReportsPagination");

                if (currentPage > 0)
                {
                    elements.Add(new CuiButton
                    {
                        Button = {
                            Command = "adminpanel.execute prevreportspage",
                            Color = configData.UI.PrimaryColor
                        },
                        RectTransform = { AnchorMin = "0.2 0.2", AnchorMax = "0.35 0.8" },
                        Text = {
                            Text = "◄ Previous",
                            FontSize = 14,
                            Align = TextAnchor.MiddleCenter,
                            Color = configData.UI.TextColor,
                            Font = configData.UI.FontName
                        }
                    }, "ReportsPagination");
                }

                if (currentPage < totalPages - 1)
                {
                    elements.Add(new CuiButton
                    {
                        Button = {
                            Command = "adminpanel.execute nextreportspage",
                            Color = configData.UI.PrimaryColor
                        },
                        RectTransform = { AnchorMin = "0.65 0.2", AnchorMax = "0.8 0.8" },
                        Text = {
                            Text = "Next ►",
                            FontSize = 14,
                            Align = TextAnchor.MiddleCenter,
                            Color = configData.UI.TextColor,
                            Font = configData.UI.FontName
                        }
                    }, "ReportsPagination");
                }
            }
        }

        private string TruncateText(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return "Unknown";
            if (text.Length <= maxLength) return text;
            return text.Substring(0, maxLength - 3) + "...";
        }

        private void CreateReportCell(CuiElementContainer elements, string text, float xPos, float width, string parent)
        {
            elements.Add(new CuiLabel
            {
                Text = {
                    Text = text,
                    FontSize = 11,
                    Align = TextAnchor.MiddleLeft,
                    Color = configData.UI.TextColor,
                    Font = configData.UI.FontName
                },
                RectTransform = { AnchorMin = $"{xPos + 0.005} 0.15", AnchorMax = $"{xPos + width - 0.005} 0.85" }
            }, parent);
        }

        private void CreateReportActionButtons(CuiElementContainer elements, PlayerReport report, float xPos, float width, string parent)
        {
            elements.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = $"{xPos + 0.005} 0.15", AnchorMax = $"{xPos + width - 0.005} 0.85" }
            }, parent, $"{parent}_Actions");

            elements.Add(new CuiButton
            {
                Button = {
                    Command = $"adminpanel.execute viewreport {report.reportId}",
                    Color = configData.UI.SecondaryColor
                },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0.48 1" },
                Text = {
                    Text = "View",
                    FontSize = 10,
                    Align = TextAnchor.MiddleCenter,
                    Color = configData.UI.TextColor,
                    Font = configData.UI.FontName
                }
            }, $"{parent}_Actions");

            elements.Add(new CuiButton
            {
                Button = {
                    Command = $"adminpanel.execute deletereport {report.reportId}",
                    Color = configData.UI.DangerColor
                },
                RectTransform = { AnchorMin = "0.52 0", AnchorMax = "1 1" },
                Text = {
                    Text = "Delete",
                    FontSize = 10,
                    Align = TextAnchor.MiddleCenter,
                    Color = configData.UI.TextColor,
                    Font = configData.UI.FontName
                }
            }, $"{parent}_Actions");
        }

        private class PlayerReport
        {
            public uint reportId;
            public string reporterName;
            public string targetName;
            public string targetId;
            public string type;
            public string subject;
            public string message;
            public DateTime timestamp;
        }

        private List<PlayerReport> GetPlayerReports()
        {
            return configData.StoredReports.OrderByDescending(r => r.timestamp).ToList();
        }

        #endregion

        #region UI Helper Methods

        private void DestroyUI(BasePlayer player)
        {
            if (player == null) return;
            CuiHelper.DestroyUi(player, PanelName);
        }

        private void StartStatsTimer(BasePlayer player)
        {
            if (player == null) return;

            if (statsTimer != null)
            {
                statsTimer.Destroy();
                statsTimer = null;
            }

            statsTimer = timer.Every(3f, () =>
            {
                if (player == null || !adminPanelStates.ContainsKey(player.userID) || !adminPanelStates[player.userID] ||
                    !currentPanel.ContainsKey(player.userID) || currentPanel[player.userID] != DashboardName)
                {
                    if (statsTimer != null)
                    {
                        statsTimer.Destroy();
                        statsTimer = null;
                    }
                    return;
                }

                UpdateStatsValues(player);
            });
        }

        private void UpdateStatsValues(BasePlayer player)
        {
            if (player == null || !statsLoaded) return;

            float fps = Performance.current.frameRate;
            int players = BasePlayer.activePlayerList.Count;
            int entities = BaseNetworkable.serverEntities.Count;
            UpdateStatCard(player, "Players Online", players.ToString());
            UpdateStatCard(player, "Server FPS", $"{fps:F1}");
            UpdateStatCard(player, "Entities", entities.ToString("N0"));

            if (TOD_Sky.Instance != null)
            {
                UpdateStatCard(player, "Server Time", $"{TOD_Sky.Instance.Cycle.Hour:F1}:00");
            }

            CuiHelper.DestroyUi(player, "TopInfoBar");
            var topInfoElement = new CuiElementContainer();

            topInfoElement.Add(new CuiPanel
            {
                Image = { Color = $"0 0 0 0.3" },
                RectTransform = { AnchorMin = "0 0.95", AnchorMax = "1 1" }
            }, ContentName, "TopInfoBar");

            topInfoElement.Add(new CuiLabel
            {
                Text = {
                    Text = $"FPS: {fps:F1} | Players: {players}/{ConVar.Server.maxplayers} | {DateTime.Now:HH:mm:ss}",
                    FontSize = 12,
                    Align = TextAnchor.MiddleLeft,
                    Color = configData.UI.SubtitleColor,
                    Font = configData.UI.FontName
                },
                RectTransform = { AnchorMin = "0.05 0", AnchorMax = "0.7 1" }
            }, "TopInfoBar");

            CuiHelper.AddUi(player, topInfoElement);
        }

        private void UpdateStatCard(BasePlayer player, string title, string value)
        {
            int cardIndex = -1;
            if (title == "Players Online") cardIndex = 0;
            else if (title == "Server FPS") cardIndex = 1;
            else if (title == "Entities") cardIndex = 2;
            else if (title == "Server Time") cardIndex = 3;

            if (cardIndex == -1) return;
            CuiHelper.DestroyUi(player, $"StatsCardValue_{cardIndex}");

            var element = new CuiElementContainer();
            element.Add(new CuiLabel
            {
                Text = {
                    Text = value,
                    FontSize = 22,
                    Align = TextAnchor.LowerLeft,
                    Color = configData.UI.TextColor,
                    Font = "robotocondensed-bold.ttf"
                },
                RectTransform = { AnchorMin = "0.05 0.1", AnchorMax = "0.7 0.6" }
            }, $"StatsCard_{cardIndex}", $"StatsCardValue_{cardIndex}");

            CuiHelper.AddUi(player, element);
        }

        #endregion

        void OnPlayerInput(BasePlayer player, InputState input)
        {
            if (player == null || input == null) return;

            if (entityKillModeStates.ContainsKey(player.userID) && entityKillModeStates[player.userID])
            {
                if (input.IsDown(BUTTON.FIRE_PRIMARY))
                {
                    KillEntityAtCrosshair(player);
                }
            }
        }

        private void KillEntityAtCrosshair(BasePlayer player)
        {
            if (player == null) return;

            RaycastHit hit;
            if (Physics.Raycast(player.eyes.HeadRay(), out hit, 100f))
            {
                var entity = hit.GetEntity();
                if (entity != null && !(entity is BasePlayer))
                {
                    PrintToChat(player, $"Killed entity: {entity.ShortPrefabName}");
                    entity.Kill();
                }
            }
        }

        private void ToggleAdminPanel(BasePlayer player)
        {
            if (player == null) return;

            if (!adminPanelStates.ContainsKey(player.userID))
            {
                adminPanelStates[player.userID] = false;
                currentPanel[player.userID] = DashboardName;
                playerListPage[player.userID] = 0;
            }

            if (adminPanelStates[player.userID])
            {
                DestroyUI(player);
                adminPanelStates[player.userID] = false;
            }
            else
            {
                CreateUI(player);
                adminPanelStates[player.userID] = true;
            }
        }

        [ChatCommand("ap")]
        void AdminPanelCommand(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;

            if (!IsPlayerAllowed(player))
            {
                SendReply(player, "You don't have permission to use the admin panel.");
                return;
            }

            ToggleAdminPanel(player);
        }

        [ConsoleCommand("adminpanel.close")]
        void CloseAdminPanel(ConsoleSystem.Arg arg)
        {
            var player = arg?.Player();
            if (player == null) return;

            DestroyUI(player);
            adminPanelStates[player.userID] = false;
        }

        [ConsoleCommand("adminpanel.execute")]
        void ExecuteCommand(ConsoleSystem.Arg arg)
        {
            var player = arg?.Player();
            if (player == null || !IsPlayerAllowed(player))
                return;

            string command = arg.GetString(0);
            string args = arg.GetString(1, "");

            try
            {
                switch (command)
                {
                    case "switchpanel":
                        if (!string.IsNullOrEmpty(args))
                        {
                            currentPanel[player.userID] = args;
                            DestroyUI(player);
                            CreateUI(player);
                        }
                        break;
                    case "refreshplayers":
                        DestroyUI(player);
                        CreateUI(player);
                        break;
                    case "refreshreports":
                        DestroyUI(player);
                        CreateUI(player);
                        break;
                    case "prevreportspage":
                        if (reportsPage.ContainsKey(player.userID) && reportsPage[player.userID] > 0)
                        {
                            reportsPage[player.userID]--;
                            DestroyUI(player);
                            CreateUI(player);
                        }
                        break;
                    case "nextreportspage":
                        if (reportsPage.ContainsKey(player.userID))
                        {
                            reportsPage[player.userID]++;
                            DestroyUI(player);
                            CreateUI(player);
                        }
                        break;
                    case "viewreport":
                        if (uint.TryParse(args, out uint viewReportId))
                        {
                            ShowReportDetails(player, viewReportId);
                        }
                        break;
                    case "settime":
                        if (float.TryParse(args, out float hour))
                        {
                            SetTime(hour);
                            DestroyUI(player);
                            CreateUI(player);
                        }
                        break;
                    case "entitykill":
                        if (!HasPermission(player, PermissionEntityKill)) break;

                        if (!entityKillModeStates.ContainsKey(player.userID))
                            entityKillModeStates[player.userID] = false;

                        entityKillModeStates[player.userID] = !entityKillModeStates[player.userID];

                        if (entityKillModeStates[player.userID])
                        {
                            PrintToChat(player, "<color=#ff4444>Entity Kill Mode ENABLED</color> - Left click to kill entities");
                        }
                        else
                        {
                            PrintToChat(player, "<color=#44ff44>Entity Kill Mode DISABLED</color>");
                        }

                        UpdateEntityKillButton(player);
                        break;
                    case "deletereport":
                        if (!HasPermission(player, PermissionReports)) break;
                        if (uint.TryParse(args, out uint reportId))
                        {
                            DeletePlayerReport(reportId);
                            PrintToChat(player, $"Deleted report #{reportId}");
                            DestroyUI(player);
                            CreateUI(player);
                        }
                        break;
                    case "maxhealth":
                        if (!HasPermission(player, PermissionMaxHealth)) break;
                        player.health = 100f;
                        player.metabolism.calories.value = 500f;
                        player.metabolism.hydration.value = 250f;
                        break;
                    case "godmode":
                        if (!HasPermission(player, PermissionGodmode)) break;
                        ToggleGodmode(player);
                        break;
                    case "vanish":
                        if (!HasPermission(player, PermissionVanish)) break;
                        ToggleVanish(player);
                        break;
                    case "noclip":
                        if (!HasPermission(player, PermissionNoclip)) break;
                        player.SendConsoleCommand("noclip");
                        break;
                    case "spectate":
                        if (!HasPermission(player, PermissionSpectate)) break;
                        player.SendConsoleCommand("spectate");
                        break;
                    case "stopspectate":
                        if (!HasPermission(player, PermissionSpectate)) break;
                        player.SendConsoleCommand("spectate");
                        player.SendConsoleCommand("respawn");
                        break;
                    case "kickplayer":
                        if (!HasPermission(player, PermissionKick)) break;
                        if (ulong.TryParse(args, out ulong kickTargetId))
                        {
                            var target = BasePlayer.FindByID(kickTargetId);
                            if (target != null)
                                Network.Net.sv.Kick(target.net.connection, "Kicked by admin");
                        }
                        break;
                    case "prevpage":
                        if (playerListPage.ContainsKey(player.userID) && playerListPage[player.userID] > 0)
                        {
                            playerListPage[player.userID]--;
                            DestroyUI(player);
                            CreateUI(player);
                        }
                        break;
                    case "nextpage":
                        if (playerListPage.ContainsKey(player.userID))
                        {
                            playerListPage[player.userID]++;
                            DestroyUI(player);
                            CreateUI(player);
                        }
                        break;
                    case "killplayer":
                        if (!HasPermission(player, PermissionKick)) break;
                        if (ulong.TryParse(args, out ulong targetId))
                        {
                            var target = BasePlayer.FindByID(targetId);
                            if (target != null)
                                target.Die();
                        }
                        break;
                    case "tpto":
                        if (!HasPermission(player, PermissionTeleport)) break;
                        if (ulong.TryParse(args, out targetId))
                        {
                            var target = BasePlayer.FindByID(targetId);
                            if (target != null)
                                player.Teleport(target.transform.position);
                        }
                        break;
                    case "tphere":
                        if (!HasPermission(player, PermissionTeleport)) break;
                        if (ulong.TryParse(args, out targetId))
                        {
                            var target = BasePlayer.FindByID(targetId);
                            if (target != null)
                                target.Teleport(player.transform.position);
                        }
                        break;
                    case "feed":
                        if (!HasPermission(player, PermissionFeed)) break;
                        if (ulong.TryParse(args, out targetId))
                        {
                            var target = BasePlayer.FindByID(targetId);
                            if (target != null)
                            {
                                target.metabolism.calories.value = 500f;
                                target.metabolism.hydration.value = 250f;
                            }
                        }
                        break;
                    case "viewinv":
                        if (!HasPermission(player, PermissionInventoryViewer))
                        {
                            PrintToChat(player, "You don't have permission to view inventories.");
                            break;
                        }
                        if (ulong.TryParse(args, out targetId))
                        {
                            var target = BasePlayer.FindByID(targetId);
                            if (target != null)
                            {
                                if (InventoryViewer != null && InventoryViewer.IsLoaded)
                                {
                                    DestroyUI(player);
                                    adminPanelStates[player.userID] = false;

                                    timer.Once(0.1f, () => {
                                        if (player != null && player.IsConnected)
                                        {
                                            var result = InventoryViewer.Call("_ViewInventory", player, target);
                                            if (result == null)
                                            {
                                                player.SendConsoleCommand($"viewinv \"{target.displayName}\"");
                                            }
                                        }
                                    });
                                }
                                else
                                {
                                    PrintToChat(player, "<color=#ff4444>InventoryViewer plugin not found or not loaded.</color>");
                                    PrintToChat(player, "<color=#ffaa44>Please install InventoryViewer plugin for this feature to work.</color>");
                                }
                            }
                            else
                            {
                                PrintToChat(player, "Target player not found.");
                            }
                        }
                        break;
                    default:
                        PrintError($"Unknown command: {command}");
                        break;
                }
            }
            catch (System.Exception ex)
            {
                PrintError($"Error executing command {command}: {ex.Message}");
            }
        }

        private void DeletePlayerReport(uint reportId)
        {
            try
            {
                var report = configData.StoredReports.FirstOrDefault(r => r.reportId == reportId);
                if (report != null)
                {
                    configData.StoredReports.Remove(report);
                    SaveConfig();
                    PrintToConsole($"[AdminPanel] Deleted report #{reportId}");
                }
            }
            catch (System.Exception ex)
            {
                PrintError($"Error deleting report: {ex.Message}");
            }
        }

        private void SetTime(float hour)
        {
            if (TOD_Sky.Instance != null)
            {
                TOD_Sky.Instance.Cycle.Hour = hour;
            }
        }
    }
}