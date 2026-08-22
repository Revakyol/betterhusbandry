using System;
using System.Linq;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.MathTools;
using Vintagestory.API.Client;
using ConfigLib;
using Vintagestory.API.Common.Entities;


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
            if(api.ModLoader.IsModEnabled("configLib"))
            {
                SubscribeToConfigChange(api);
            }
        }

        private void SubscribeToConfigChange(ICoreAPI api)
        {
            if (Config == null) return;
            var configLib = api.ModLoader.GetModSystem<ConfigLibModSystem>();
            if(configLib != null)
            {
                configLib.SettingChanged += (domain, config, setting) =>
                {
                    if(domain != "betterhusbandry") return;
                    setting.AssignSettingValue(Config);
                };
                configLib.ConfigsLoaded += () =>
                {
                    configLib.GetConfig("betterhusbandry")?.AssignSettingsValues(Config);
                };
            }
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);
            sapi = api;

            var parsers = api.ChatCommands.Parsers;

            api.ChatCommands.Create("betterhusbandry")
                .WithDescription("Debug/Admin commands for the Better Husbandry mod")
                .RequiresPrivilege(Privilege.controlserver)
                .BeginSubCommand("setattr")
                    .WithArgs(
                        parsers.Entities("target"),
                        parsers.Word("attr", new[] {"bloodline", "feed", "interact"}),
                        parsers.Int("value")
                    )
                    .HandleWith(OnSetAttr)
                    .WithDescription("Sets the given Better Husbandry Attribute on a given entity")
                .EndSubCommand()
                .BeginSubCommand("attr")
                    .WithArgs(
                        parsers.Entities("target"),
                        parsers.OptionalWord("attr")
                    )
                    .HandleWith(OnGetAttr)
                    .WithDescription("Shows the given Better Husbandry Attribute on a given entity. If no Attributes are given, shows them all.")
                .EndSubCommand()
                .WithAlias("bh");

            LoadConfig();

            serverChannel = api.Network.GetChannel("betterhusbandry");
            api.Event.PlayerJoin += OnPlayerJoin;

            api.Event.RegisterGameTickListener(OnConfigReloadTick, (int)(ConfigReloadIntervalSeconds * 1000));
            api.Event.ServerRunPhase(EnumServerRunPhase.Shutdown, OnServerShutdown);

            RegisterDailyCareTickListener();

            harmony = new Harmony("betterhusbandry");
            harmony.PatchAll();

            if (IsShearLibPresent())
            {
                var shearLibAsm = AppDomain.CurrentDomain.GetAssemblies()
                    .First(a => a.GetName().Name == "vs_shearlib");
                var targetType = shearLibAsm.GetType("ShearLib.EntityBehaviorShearable");
                var targetMethod = AccessTools.Method(targetType, "DoShear");

                var postfix = new HarmonyMethod(typeof(HarmonyPatches.Patch_OnShear), nameof(HarmonyPatches.Patch_OnShear.Postfix));
                harmony.Patch(targetMethod, prefix: postfix);
            }
        }

        // For checking if the ShearLib is present, so we can patch its felling method for bonus drops
        static bool IsShearLibPresent()
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Any(a => a.GetName().Name == "vs_shearlib");
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            clientChannel = api.Network.GetChannel("betterhusbandry");
            clientChannel.SetMessageHandler<CapsPacket>(OnCapsReceived);
        }

        private void OnServerShutdown()
        {
            foreach (var entity in sapi.World.LoadedEntities.Values)
            {
                var behavior = entity.GetBehavior<EntityBehaviorbetterhusbandry>();
                if (behavior == null) continue;

                entity.WatchedAttributes.SetInt("generation", behavior.Bloodline);
            }
        }

        private TextCommandResult OnSetAttr(TextCommandCallingArgs args)
        {
            var entities = (Entity[])args[0];
            string attr = (string)args[1];
            int value = (int)args[2];

            if(entities.Length == 0) return TextCommandResult.Error("No Entities Matched");
            int affected = 0;
            foreach (var entity in entities)
            {
                var bh = entity.GetBehavior<EntityBehaviorbetterhusbandry>();
                if (bh == null) continue;

                switch (attr)
                {
                    case "bloodline": bh.Bloodline = value; break;
                    case "feed": bh.FeedMod = value; break;
                    case "interact": bh.InteractMod = value; break;
                    default: return TextCommandResult.Error("attr must be one of: bloodline, feed, interact");
                }
                affected++;
                entity.WatchedAttributes.MarkPathDirty("betterhusbandry");
                bh.RecomputeEffectiveGeneration();
            }

            return TextCommandResult.Success($"Set {attr} = {value} on {affected} entit{(affected == 1 ? "y" : "ies")}.");
        }

        private TextCommandResult OnGetAttr(TextCommandCallingArgs args)
        {
            
            var entities = (Entity[])args[0];
            bool attrGiven = !args.Parsers[1].IsMissing;
            string attr = attrGiven ? (string)args[1] : null;
            if (attr != null && attr != "bloodline" && attr != "feed" && attr != "interact")
                return TextCommandResult.Error("attr must be one of: bloodline, feed, interact");


            if (entities.Length == 0) return TextCommandResult.Error("No entities matched.");
            
            string output = "";
            foreach (var entity in entities)
            {
                var bh = entity.GetBehavior<EntityBehaviorbetterhusbandry>();
                if (bh == null) continue;
                output += $"Entity {entity.EntityId}:";
                switch(attr)
                {
                    case null: output += $"  bloodline: {bh.Bloodline},  feed: {bh.FeedMod},  interact: {bh.InteractMod}"; break;
                    case "bloodline": output += $"  bloodline: {bh.Bloodline}"; break;
                    case "feed": output += $"  feed: {bh.FeedMod}"; break;
                    case "interact": output += $"  interact: {bh.InteractMod}"; break;
                }
            }

            return TextCommandResult.Success(output);
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
                InteractFloor = Config.interactFloor,
                InteractCeiling = Config.interactCeiling,
                FeedFloor = Config.feedFloor,
                FeedCeiling = Config.feedCeiling
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
                    Config.version = 2;
                    sapi.StoreModConfig(Config, ConfigFileName);
                    sapi.Logger.Notification("[betterhusbandry] No config found, wrote defaults to {0}.", ConfigFileName);
                }
                else if(Config.version != 2)
                {
                    //We have an older version of the config file that needs migrated, load it into the old class structure
                    oldbetterhusbandryConfig OldConfig = sapi.LoadModConfig<oldbetterhusbandryConfig>(ConfigFileName);
                    //Set Version
                    Config.version = 2;
                    //Migrate feed values
                    Config.feedCeiling = OldConfig.FeedMod.Ceiling;
                    Config.feedFloor = OldConfig.FeedMod.Floor;
                    Config.feedGrowthPerDay = OldConfig.FeedMod.GrowthPerDay;
                    Config.feedDecayPerDay = OldConfig.FeedMod.DecayPerDay;
                    Config.feedGoodWeightThreshold = OldConfig.FeedMod.GoodWeightThreshold;
                    Config.feedOkWeightThreshold = OldConfig.FeedMod.OkWeightThreshold;
                    //Then migrate interact values
                    Config.interactCeiling = OldConfig.InteractMod.Ceiling;
                    Config.interactFloor = OldConfig.InteractMod.Floor;
                    Config.interactGrowthPerDay = OldConfig.InteractMod.GrowthPerDay;
                    Config.interactDecayPerDay = OldConfig.InteractMod.DecayPerDay;
                    Config.interactGraceDays = OldConfig.InteractMod.GraceDays;
                    Config.interactgenerationDecayMultiplier = OldConfig.InteractMod.generationDecayMultiplier;
                    //Before saving to file, we want to convert it to the newer Config file, so we can deprecate the old config.
                    sapi.StoreModConfig(Config, ConfigFileName);
                    sapi.Logger.Notification("[betterhusbandry] Old Config Found at {0}, Migrated values to new config.", ConfigFileName);
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
            if (Config == null) return;
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
