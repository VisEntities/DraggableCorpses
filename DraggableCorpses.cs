using Newtonsoft.Json;
using UnityEngine;
using System.Collections.Generic;
using System;
using System.Linq;

namespace Oxide.Plugins
{
    [Info("Draggable Corpses", "Dana", "2.0.0")]
    [Description("Bring corpses to life and take them for a walk.")]

    public class DraggableCorpses : RustPlugin
    {
        #region Fields

        private static DraggableCorpses _instance;
        private static Configuration _config;
        private CorpseDragController _corpseDragController;

        #endregion Fields

        #region Configuration

        private class Configuration
        {
            [JsonProperty("Version")]
            public string Version { get; set; }

            [JsonProperty("Drag Button")]
            public string DragButton { get; set; }

            [JsonIgnore]
            public BUTTON Button
            {
                get
                {
                    return (BUTTON)Enum.Parse(typeof(BUTTON), DragButton);
                }
            }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            _config = Config.ReadObject<Configuration>();

            if (string.Compare(_config.Version, Version.ToString()) < 0)
                UpdateConfig();

            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            _config = GetDefaultConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config, true);
        }

        private void UpdateConfig()
        {
            PrintWarning("Detected changes in configuration! Updating...");
            Configuration defaultConfig = GetDefaultConfig();

            if (string.Compare(_config.Version, "1.0.0") < 0)
                _config = defaultConfig;

            PrintWarning("Configuration update complete! Updated from version " + _config.Version + " to " + Version.ToString());
            _config.Version = Version.ToString();
        }

        private Configuration GetDefaultConfig()
        {
            return new Configuration
            {
                Version = Version.ToString(),
                DragButton = "FIRE_THIRD",
            };
        }

        #endregion Configuration

        #region Oxide Hooks

        private void Init()
        {
            _instance = this;
            _corpseDragController = new CorpseDragController();

            PermissionUtils.Register();
        }

        private void Unload()
        {
            _config = null;
            _instance = null;

            _corpseDragController.StopDraggingForAll();
        }

        private object CanLootEntity(BasePlayer player, PlayerCorpse corpse)
        {
            if (!player.IsValid())
                return null;

            if (corpse == null)
                return null;

            if (!PermissionUtils.Verify(player))
                return null;

            if (!player.serverInput.IsDown(_config.Button))
                return null;

            _corpseDragController.StartDragging(corpse, player);
            return false;
        }

        #endregion Oxide Hooks

        #region Corpse Drag Controller

        private class CorpseDragController
        {
            private Dictionary<BasePlayer, CorpseDragComponent> _components = new Dictionary<BasePlayer, CorpseDragComponent>();

            public void RegisterDragger(CorpseDragComponent component)
            {
                _components[component.Dragger] = component;
            }

            public void UnregisterDragger(CorpseDragComponent component)
            {
                _components.Remove(component.Dragger);
            }

            public bool StartDragging(PlayerCorpse corpse, BasePlayer player)
            {
                if (PlayerIsDraggingCorpse(player))
                    return false;

                CorpseDragComponent.InstallComponent(corpse, player, this);
                return true;
            }

            public bool CorpseBeingDragged(PlayerCorpse corpse)
            {
                if (!corpse.IsValid() || corpse.IsDestroyed)
                    return false;

                return CorpseDragComponent.GetComponent(corpse) != null;
            }

            public bool PlayerIsDraggingCorpse(BasePlayer player)
            {
                return GetComponentForPlayer(player) != null;
            }

            public CorpseDragComponent GetComponentForPlayer(BasePlayer player)
            {
                CorpseDragComponent component;
                return _components.TryGetValue(player, out component) ? component : null;
            }

            public void StopDraggingForPlayer(BasePlayer player)
            {
                GetComponentForPlayer(player)?.DestroyComponent();
            }

            public void StopDraggingForCorpse(PlayerCorpse corpse)
            {
                CorpseDragComponent.GetComponent(corpse)?.DestroyComponent();
            }

            public void StopDraggingForAll()
            {
                foreach (CorpseDragComponent component in _components.Values.ToArray())
                {
                    component.DestroyComponent();
                }
            }
        }

        #endregion Corpse Drag Controller

        #region Corpse Drag Component

        private class CorpseDragComponent : FacepunchBehaviour
        {
            private CorpseDragController _corpseDragController;
            private static int _raycastLayers = LayerMask.GetMask("Construction", "Deployed", "Default", "Debris", "Terrain", "Tree", "World");

            public BasePlayer Dragger { get; set; }
            public PlayerCorpse Corpse { get; set; }

            #region Corpse Functions

            private void StopDraggingCorpse()
            {
                _corpseDragController.UnregisterDragger(this);
                DestroyComponent();
            }

            private void UpdateCorpsePosition()
            {
                Vector3 targetPosition = Dragger.eyes.position + Dragger.eyes.BodyForward() * 2f + Vector3.up * 1f;

                RaycastHit raycastHit;
                if (Physics.Raycast(Dragger.eyes.BodyRay(), out raycastHit, 3f, _raycastLayers))
                    targetPosition = raycastHit.point - Dragger.eyes.BodyForward();

                Corpse.transform.position = targetPosition;
            }

            #endregion Corpse Function

            #region Component Lifecycle

            private void Update()
            {
                if (!Dragger || Dragger.IsDead() || !Dragger.IsConnected || !Corpse || Corpse.IsDestroyed)
                {
                    DestroyComponent();
                    return;
                }

                if (!Dragger.serverInput.IsDown(_config.Button))
                    StopDraggingCorpse();

                UpdateCorpsePosition();
            }

            private void OnDestroy()
            {
                _corpseDragController.UnregisterDragger(this);
            }

            public static void InstallComponent(PlayerCorpse corpse, BasePlayer player, CorpseDragController corpseDragController)
            {
                corpse.gameObject.AddComponent<CorpseDragComponent>().InitializeComponent(player, corpseDragController);
            }

            public CorpseDragComponent InitializeComponent(BasePlayer player, CorpseDragController corpseDragController)
            {
                Corpse = GetComponent<PlayerCorpse>();
                Dragger = player;

                _corpseDragController = corpseDragController;
                _corpseDragController.RegisterDragger(this);

                return this;
            }

            public static CorpseDragComponent GetComponent(PlayerCorpse corpse)
            {
                return corpse.gameObject.GetComponent<CorpseDragComponent>();
            }

            public void DestroyComponent()
            {
                DestroyImmediate(this);
            }

            #endregion Component Lifecycle
        }

        #endregion Corpse Drag Component

        #region Helper Classes

        private static class PermissionUtils
        {
            public const string USE = "draggablecorpses.use";

            public static void Register()
            {
                _instance.permission.RegisterPermission(USE, _instance);
            }

            public static bool Verify(BasePlayer player, string permissionName = USE)
            {
                if (_instance.permission.UserHasPermission(player.UserIDString, permissionName))
                    return true;

                return false;
            }
        }

        #endregion Helper Classes
    }
}