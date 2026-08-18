using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace betterhusbandry
{
    /// <summary>
    /// Config for a single modifier axis (feed or interact). All values are
    /// player-editable via VintagestoryData/ModConfig/betterhusbandry.json and
    /// reloaded live - see betterhusbandryModSystem.OnConfigReloadTick.
    /// </summary>
    public class ModifierConfig
    {
        public float Floor = -3f;
        public float Ceiling = 3f;

        /// <summary>Modifier gained per in-game day with a qualifying action.</summary>
        public float GrowthPerDay = 0.03f;

        /// <summary>Modifier lost per in-game day without a qualifying action (after GraceDays).</summary>
        public float DecayPerDay = 0.2f;

        /// <summary>In-game days after the last qualifying action before decay starts.</summary>
        public float GraceDays = 1f;

        /// <summary>
        /// Multiplier for how many in-game days of grace the animal gets per
        /// generation. For example, if this is 0.5 and the animal is generation 2, it will have 1 extra day of grace before decay starts.
        /// </summary
        public float generationDecayMultiplier = 0.5f;

        /// <summary>
        /// Clamps this config to sane hard bounds so a bad manual edit can't
        /// produce broken or crash-prone behavior. Called after every load
        /// and every reload, never trust the file blindly.
        /// </summary>
        public void Sanitize(ILogger logger, string label)
        {
            const float hardBound = 10f;

            float origFloor = Floor;
            float origCeiling = Ceiling;

            Floor = GameMath.Clamp(Floor, -hardBound, hardBound);
            Ceiling = GameMath.Clamp(Ceiling, -hardBound, hardBound);

            if (Floor > Ceiling)
            {
                logger?.Warning("[betterhusbandry] {0}.Floor ({1}) was greater than {0}.Ceiling ({2}). Swapping them.",
                    label, Floor, Ceiling);
                float tmp = Floor;
                Floor = Ceiling;
                Ceiling = tmp;
            }

            if (GrowthPerDay < 0)
            {
                logger?.Warning("[betterhusbandry] {0}.GrowthPerDay was negative, clamping to 0.", label);
                GrowthPerDay = 0;
            }

            if (DecayPerDay < 0)
            {
                logger?.Warning("[betterhusbandry] {0}.DecayPerDay was negative, clamping to 0.", label);
                DecayPerDay = 0;
            }

            if (GraceDays < 0)
            {
                GraceDays = 0;
            }

            if (origFloor != Floor || origCeiling != Ceiling)
            {
                logger?.Warning("[betterhusbandry] {0} bounds clamped to hard limits: Floor={1}, Ceiling={2}.",
                    label, Floor, Ceiling);
            }
        }
    }

    public class FeedModifierConfig : ModifierConfig
    {
        /// <summary>
        /// If true, the feed modifier will only grow if the animal is at or
        /// above a certain weight threshold. If false, the feed modifier will
        /// grow regardless of weight.
        /// </summary>
        public float GoodWeightThreshold = 0.95f;

        public float OkWeightThreshold = 0.75f;

        public new void Sanitize(ILogger logger, string label)
        {
            base.Sanitize(logger, label);

            if (GoodWeightThreshold < 0 || GoodWeightThreshold > 1)
            {
                logger?.Warning("[betterhusbandry] {0}.GoodWeightThreshold was out of range [0,1], clamping.", label);
                GoodWeightThreshold = GameMath.Clamp(GoodWeightThreshold, 0f, 1f);
            }

            if (OkWeightThreshold < 0 || OkWeightThreshold > 1)
            {
                logger?.Warning("[betterhusbandry] {0}.OkWeightThreshold was out of range [0,1], clamping.", label);
                OkWeightThreshold = GameMath.Clamp(OkWeightThreshold, 0f, 1f);
            }

            if (OkWeightThreshold > GoodWeightThreshold)
            {
                logger?.Warning("[betterhusbandry] {0}.OkWeightThreshold ({1}) was greater than GoodWeightThreshold ({2}), swapping.",
                    label, OkWeightThreshold, GoodWeightThreshold);
                float tmp = OkWeightThreshold;
                OkWeightThreshold = GoodWeightThreshold;
                GoodWeightThreshold = tmp;
            }
        }
    }

    /// <summary>
    /// Root config. Deliberately does NOT expose the effective-generation
    /// floor/ceiling (0-10) - that range is hardcoded in
    /// EntityBehaviorbetterhusbandry.RecomputeEffectiveGeneration because
    /// vanilla's milking-rejection and aggression/flee tables are built
    /// around it, and a bad edit there risks an out-of-range read in code
    /// this mod doesn't control.
    /// </summary>
    public class betterhusbandryConfig : newbetterhusbandryConfig
    {
        public FeedModifierConfig FeedMod = new FeedModifierConfig();
        public ModifierConfig InteractMod = new ModifierConfig();

        public new void Sanitize(ILogger logger)
        {
            base.Sanitize(logger);
            FeedMod?.Sanitize(logger, nameof(FeedMod));
            InteractMod?.Sanitize(logger, nameof(InteractMod));
        }
    }

    public class newbetterhusbandryConfig
    {
        public int version;
        public float feedFloor = -3f;
        public float feedCeiling = 3f;

        /// <summary>Modifier gained per in-game day with a qualifying action.</summary>
        public float feedGrowthPerDay = 0.03f;

        /// <summary>Modifier lost per in-game day without a qualifying action (after GraceDays).</summary>
        public float feedDecayPerDay = 0.2f;

        public float feedGoodWeightThreshold = 0.95f;

        public float feedOkWeightThreshold = 0.75f;

        public float interactFloor = -3f;
        public float interactCeiling = 3f;

        /// <summary>Modifier gained per in-game day with a qualifying action.</summary>
        public float interactGrowthPerDay = 0.03f;

        /// <summary>Modifier lost per in-game day without a qualifying action (after GraceDays).</summary>
        public float interactDecayPerDay = 0.2f;

        /// <summary>In-game days after the last qualifying action before decay starts.</summary>
        public float interactGraceDays = 1f;

        /// <summary>
        /// Multiplier for how many in-game days of grace the animal gets per
        /// generation. For example, if this is 0.5 and the animal is generation 2, it will have 1 extra day of grace before decay starts.
        /// </summary
        public float interactgenerationDecayMultiplier = 0.5f;

        public void SanitizeFeed(ILogger logger)
        {
            if (feedFloor > feedCeiling)
            {
                logger?.Warning("[betterhusbandry] feedFloor ({0}) was greater than feedCeiling ({1}). Swapping them.",
                    feedFloor, feedCeiling);
                float tmp = feedFloor;
                feedFloor = feedCeiling;
                feedCeiling = tmp;
            }

            if (feedGrowthPerDay < 0)
            {
                logger?.Warning("[betterhusbandry] feedGrowthPerDay was negative, clamping to 0.");
                feedGrowthPerDay = 0;
            }

            if (feedDecayPerDay < 0)
            {
                logger?.Warning("[betterhusbandry] feedDecayPerDay was negative, clamping to 0.");
                feedDecayPerDay = 0;
            }

            if (feedGoodWeightThreshold < 0 || feedGoodWeightThreshold > 1)
            {
                logger?.Warning("[betterhusbandry] feedGoodWeightThreshold was out of range [0,1], clamping.");
                feedGoodWeightThreshold = GameMath.Clamp(feedGoodWeightThreshold, 0f, 1f);
            }

            if (feedOkWeightThreshold < 0 || feedOkWeightThreshold > 1)
            {
                logger?.Warning("[betterhusbandry] feedOkWeightThreshold was out of range [0,1], clamping.");
                feedOkWeightThreshold = GameMath.Clamp(feedOkWeightThreshold, 0f, 1f);
            }

            if (feedOkWeightThreshold > feedGoodWeightThreshold)
            {
                logger?.Warning("[betterhusbandry] feedOkWeightThreshold ({0}) was greater than GoodWeightThreshold ({1}), swapping.",
                    feedOkWeightThreshold, feedGoodWeightThreshold);
                float tmp = feedOkWeightThreshold;
                feedOkWeightThreshold = feedGoodWeightThreshold;
                feedGoodWeightThreshold = tmp;
            }
        } 

        public void SanitizeInteract(ILogger logger)
        {
            if (interactFloor > interactCeiling)
            {
                logger?.Warning("[betterhusbandry] interactFloor ({0}) was greater than interactCeiling ({1}). Swapping them.",
                    interactFloor, interactCeiling);
                float tmp = interactFloor;
                interactFloor = interactCeiling;
                interactCeiling = tmp;
            }

            if (interactGrowthPerDay < 0)
            {
                logger?.Warning("[betterhusbandry] interactGrowthPerDay was negative, clamping to 0.");
                interactGrowthPerDay = 0;
            }

            if (interactDecayPerDay < 0)
            {
                logger?.Warning("[betterhusbandry] interactDecayPerDay was negative, clamping to 0.");
                interactDecayPerDay = 0;
            }

            if (interactGraceDays < 0)
            {
                logger?.Warning("[betterhusbandry] interactGraceDays was negative, clamping to 0.");
                interactGraceDays = 0;
            }

            if (interactgenerationDecayMultiplier < 0)
            {
                logger?.Warning("[betterhusbandry] interactgenerationDecayMultiplier was negative, clamping to 0.");
                interactgenerationDecayMultiplier = 0;
            }
        } 

        public void Sanitize(ILogger logger)
        {
            SanitizeFeed(logger);
            SanitizeInteract(logger);
        }
    }
}
