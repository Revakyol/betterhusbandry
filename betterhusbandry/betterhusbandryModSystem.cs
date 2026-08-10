using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.MathTools;
using Vintagestory.API.Client;

namespace betterhusbandry
{
    public class betterhusbandryModSystem : ModSystem
    {
        const string ConfigFileName = "betterhusbandry.json";

        // Real-world seconds between config reload checks - cheap, so this
        // can stay frequent without cost concerns.
        const float ConfigReloadIntervalSeconds = 45f;

        ICoreServerAPI sapi= null!;
        Harmony harmony = null!;

        IServerNetworkChannel? serverChannel;
        IClientNetworkChannel? clientChannel;

        long dailyCareListenerId = -1;

        float lastKnownCalendarSpeedMul = -1f;

        public float ClientInteractFloor {get; private set;} = -3f;
        public float ClientInteractCeiling {get; private set;} = 3f;

        public float ClientFeedFloor {get; private set;} = -3f;
        public float ClientFeedCeiling {get; private set;} = 3f;

        public betterhusbandryConfig? Config { get; private set; }

        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            api.RegisterEntityBehaviorClass("betterhusbandry", typeof(EntityBehaviorbetterhusbandry));
            api.Network.RegisterChannel("betterhusbandry")
                .RegisterMessageType(typeof(CapsPacket));
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;

            LoadConfig();

            serverChannel = api.Network.GetChannel("betterhusbandry");
            api.Event.PlayerJoin += OnPlayerJoin;

            api.Event.RegisterGameTickListener(OnConfigReloadTick, (int)(ConfigReloadIntervalSeconds * 1000));

            RegisterDailyCareTickListener();

            harmony = new Harmony("betterhusbandry");
            harmony.PatchAll();
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            clientChannel = api.Network.GetChannel("betterhusbandry");
            clientChannel.SetMessageHandler<CapsPacket>(OnCapsReceived);
        }

        void OnCapsReceived(CapsPacket packet)
        {
            ClientInteractFloor = packet.InteractFloor;
            ClientInteractCeiling = packet.InteractCeiling;
            ClientFeedFloor = packet.FeedFloor;
            ClientFeedCeiling = packet.FeedCeiling;
        }

        void OnPlayerJoin(IServerPlayer player)
        {
            BroadcastCaps(player);
        }

        void BroadcastCaps(IServerPlayer? onlyTo = null)
        {
            if (serverChannel == null || Config == null) return;

            var packet = new CapsPacket
            {
                InteractFloor = Config.InteractMod.Floor,
                InteractCeiling = Config.InteractMod.Ceiling,
                FeedFloor = Config.FeedMod.Floor,
                FeedCeiling = Config.FeedMod.Ceiling
            };

            if (onlyTo != null)
            {
                serverChannel?.SendPacket(packet, onlyTo);
            }
            else
            {
                serverChannel?.BroadcastPacket(packet);
            }
        }

        void LoadConfig()
        {
            try
            {
                Config = sapi.LoadModConfig<betterhusbandryConfig>(ConfigFileName);
                if (Config == null)
                {
                    Config = new betterhusbandryConfig();
                    sapi.StoreModConfig(Config, ConfigFileName);
                    sapi.Logger.Notification("[betterhusbandry] No config found, wrote defaults to {0}.", ConfigFileName);
                }
            }
            catch (Exception e)
            {
                sapi.Logger.Error("[betterhusbandry] Failed to load {0}, falling back to defaults: {1}", ConfigFileName, e);
                Config = new betterhusbandryConfig();
            }

            Config.Sanitize(sapi.Logger);
        }

        void RegisterDailyCareTickListener()
        {
            if(sapi == null || sapi.World.Calendar == null) return;
            if (dailyCareListenerId != -1)
            {
                sapi.Event.UnregisterGameTickListener(dailyCareListenerId);
            }

            var cal = sapi.World.Calendar;
            double realMsPerDay = (cal.HoursPerDay * 3600000.0) / (cal.SpeedOfTime * cal.CalendarSpeedMul);

            //Clamp to a sane range so that a CSM near 0 does not cause a huge interval
            int intervalMs = (int)GameMath.Clamp(realMsPerDay, 1000, 24 * 3600 * 1000);
            dailyCareListenerId = sapi.Event.RegisterGameTickListener(OnDailyCareTick, intervalMs);
            lastKnownCalendarSpeedMul = cal.CalendarSpeedMul;
        }

        void OnConfigReloadTick(float dt)
        {
            LoadConfig();
            BroadcastCaps();

            if (sapi.World.Calendar.CalendarSpeedMul != lastKnownCalendarSpeedMul)
            {
                RegisterDailyCareTickListener();
            }
        }

        void OnDailyCareTick(float dt)
        {
            double nowHours = sapi.World.Calendar.TotalHours;

            foreach (var entity in sapi.World.LoadedEntities.Values)
            {
                var behavior = entity.GetBehavior<EntityBehaviorbetterhusbandry>();
                if (behavior == null) continue;

                if (!double.IsNegativeInfinity(behavior.LastUpdateHours)
                    && nowHours - behavior.LastUpdateHours < 24.0)
                {
                    continue;
                }

                behavior.ApplyDailyUpdate(nowHours, Config);
            }
        }

        public override void Dispose()
        {
            harmony?.UnpatchAll(harmony.Id);
            base.Dispose();
        }
    }
}
