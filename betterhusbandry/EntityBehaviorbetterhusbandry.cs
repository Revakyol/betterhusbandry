using System;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace betterhusbandry
{
    /// <summary>
    /// Tracks feeding and interaction modifiers for a single animal, and
    /// derives the effective generation that vanilla code reads for milking
    /// rejection chance, aggression, and flee behavior.
    ///
    /// Attach to breedable animal entity configs via JSON patch, e.g.
    /// (patches/entity-sheep-adult-female.json or similar):
    ///
    ///   {
    ///     "op": "add",
    ///     "path": "/server/behaviors/-",
    ///     "value": { "code": "betterhusbandry" }
    ///   }
    ///
    /// Attribute layout, all under entity.WatchedAttributes:
    ///   "bloodline"              (int)    - permanent pedigree ceiling, set once at birth
    ///   "generation"             (int)    - the vanilla key; we now write this, never breeding logic
    ///   "betterhusbandry" (subtree)
    ///     "feedMod"              (float)
    ///     "interactMod"          (float)
    ///     "lastFedHours"         (double) - calendar TotalHours of last qualifying feed
    ///     "lastInteractedHours"  (double) - calendar TotalHours of last qualifying interaction
    ///     "lastUpdateHours"      (double) - calendar TotalHours this behavior last ran its daily pass
    /// </summary>
    public class EntityBehaviorbetterhusbandry : EntityBehavior
    {
        const string RootKey = "betterhusbandry";

        ITreeAttribute Tree => entity.WatchedAttributes.GetOrAddTreeAttribute(RootKey);

        public float FeedMod
        {
            get => Tree.GetFloat("feedMod", 0f);
            set => Tree.SetFloat("feedMod", value);
        }

        public float InteractMod
        {
            get => Tree.GetFloat("interactMod", 0f);
            set => Tree.SetFloat("interactMod", value);
        }

        public double LastFedHours => entity.WatchedAttributes.GetDouble("lastMealEatenTotalHours", double.NegativeInfinity);

        public double LastInteractedHours
        {
            get => Tree.GetDouble("lastInteractedHours", double.NegativeInfinity);
            set => Tree.SetDouble("lastInteractedHours", value);
        }

        public double LastDecayHours
        {
            get => Tree.GetDouble("lastDecayHours", double.NegativeInfinity);
            set => Tree.SetDouble("lastDecayHours", value);
        }

        public double LastUpdateHours
        {
            get => Tree.GetDouble("lastUpdateHours", double.NegativeInfinity);
            set => Tree.SetDouble("lastUpdateHours", value);
        }

        /// <summary>
        /// Permanent pedigree ceiling. Separate from the vanilla "generation"
        /// attribute, which this mod repurposes as a derived, live value.
        /// Set once at birth (motherBloodline + 1) and never changed again.
        /// </summary>
        public int Bloodline
        {
            get => Tree.GetInt("bloodline", 0);
            set => Tree.SetInt("bloodline", value);
        }

        public EntityBehaviorbetterhusbandry(Entity entity) : base(entity) { }

        public override string PropertyName() => "betterhusbandry";

        public override void GetInfoText(StringBuilder infotext)
        {
            var modSystem = entity.Api.ModLoader.GetModSystem<betterhusbandryModSystem>();
            float feedFloor = modSystem?.ClientFeedFloor ?? 0f;
            float feedCeiling = modSystem?.ClientFeedCeiling ?? 0f;
            float interactFloor = modSystem?.ClientInteractFloor ?? 0f;
            float interactCeiling = modSystem?.ClientInteractCeiling ?? 0f;
            double hoursSinceInteract = double.IsNegativeInfinity(LastInteractedHours)
                ? double.PositiveInfinity
                : entity.World.Calendar.TotalHours - LastInteractedHours;
            double hoursPerDay = entity.World.Calendar.HoursPerDay;

            infotext.AppendLine($"Bloodline Generation: {Bloodline}");
            if(interactCeiling != 0)
            {
                infotext.AppendLine(
                    InteractMod == interactCeiling
                    ? "The animal trusts you completely"
                    : InteractMod/interactCeiling >= 0.75
                        ? "The animal trusts you"
                        : InteractMod/interactCeiling >= 0.5
                            ? "The animal is beginning to trust you"
                            : InteractMod/interactCeiling >= 0.25
                                ? "The animal is cautious around you"
                                : InteractMod/interactCeiling > 0
                                    ? "The animal is neutral to you"
                                    : InteractMod/interactCeiling >= -0.25
                                        ? "The animal is wary of you"
                                        : InteractMod/interactCeiling >= -0.5
                                            ? "The animal is avoiding you"
                                            : InteractMod/interactCeiling >= -0.75
                                                ? "The animal is beginning to fear you"
                                                : InteractMod == interactFloor
                                                    ? "The animal is terrified of you"
                                                    : "The animal is afraid of you"
                );
            }
            infotext.AppendLine(hoursSinceInteract <= hoursPerDay
                ? $"Last interacted {hoursSinceInteract:0.0} hours ago"
                : "Last interacted more than one day ago");

            base.GetInfoText(infotext);
        }


        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);

            // GiveBirth sets both of these on the newborn, in this order,
            // before SpawnEntity triggers this Initialize call. Only
            // fresh births carry "origin" == "reproduction"
            // guard on bloodline being unset so this is a no-op on subseqwuent Initialize calls (e.g. on reload).
            if (Bloodline == 0 && entity.Attributes.GetString("origin") == "reproduction")
            {
                Bloodline = entity.WatchedAttributes.GetInt("generation", 0);
            }
        }

        /// <summary>Call whenever the player successfully milks or pets the animal.</summary>
        public void RegisterInteract(double nowHours)
        {
            LastInteractedHours = nowHours;
            entity.WatchedAttributes.MarkPathDirty(RootKey);
        }

        /// <summary>Call whenever the animal's Interaction modifier decays.</summary>
        public void RegisterDecay(double nowHours)
        {
            LastDecayHours = nowHours;
            entity.WatchedAttributes.MarkPathDirty(RootKey);
        }

        /// <summary>
        /// Applies one in-game day's worth of growth/decay to both modifiers
        /// based on whether a qualifying feed/interaction happened since the
        /// last call, then re-derives and writes the vanilla "generation"
        /// attribute. Intended to be called once per in-game day per animal,
        /// not per tick - see betterhusbandryModSystem.OnDailyCareTick.
        /// </summary>
        public void ApplyDailyUpdate(double nowHours, betterhusbandryConfig config)
        {
            FeedMod = ApplyFeedAxis(FeedMod, config);
            InteractMod = ApplyAxis(InteractMod, LastInteractedHours, LastDecayHours, nowHours, entity.World.Calendar.HoursPerDay, config);
            entity.WatchedAttributes.MarkPathDirty(RootKey);

            RecomputeEffectiveGeneration();
            LastUpdateHours = nowHours;
        }

        float ApplyFeedAxis(float current, betterhusbandryConfig cfg)
        {
            float weight = entity.WatchedAttributes.GetFloat("animalWeight", 1f);

            float updated;
            if (weight >= 0.95f) updated = current + cfg.feedGrowthPerDay; //Creature has good weight, so we reward the player for feeding it.
            else if (weight >= 0.75f) updated = current; //Creature is underweight, but not starving, so we don't reward or punish the player for feeding it.
            else updated = current - cfg.feedDecayPerDay; //Creature is starving, so we punish the player for not feeding it.

            return GameMath.Clamp(updated, cfg.feedFloor, cfg.feedCeiling);
        }

        float ApplyAxis(float current, double lastActionHours, double lastDecayHours, double nowHours, double hoursPerDay, betterhusbandryConfig cfg)
        {
            double hoursSinceAction = double.IsNegativeInfinity(lastActionHours)
                ? double.PositiveInfinity
                : nowHours - lastActionHours;
            double hoursSinceLastDecay = double.IsNegativeInfinity(lastDecayHours)
                ? double.PositiveInfinity
                : nowHours - lastDecayHours;

            double graceHours = cfg.interactGraceDays * hoursPerDay;
            int generation = entity.WatchedAttributes.GetInt("generation", 0);
            double decayHours = generation * hoursPerDay * cfg.interactgenerationDecayMultiplier; // Effective generation is the number of days the animal can go without being interacted with before it starts to decay.

            float updated;
            if (hoursSinceAction <= hoursPerDay)
            {
                updated = current + cfg.interactGrowthPerDay;
            }
            else if (hoursSinceAction <= hoursPerDay + graceHours + decayHours || hoursSinceLastDecay <= hoursPerDay + graceHours + decayHours)
            {
                // Within the configured grace window - hold steady.
                updated = current;
            }
            else
            {
                // Neglected beyond the grace window - decay. Can go negative,
                // clamped at cfg.Floor.
                updated = current - cfg.interactDecayPerDay;
                RegisterDecay(nowHours); // Reset the last-decay timestamp so we don't keep decaying every day after this.
            }

            return GameMath.Clamp(updated, cfg.interactFloor, cfg.interactCeiling);
        }

        /// <summary>
        /// effective = clamp(bloodline + feedMod + interactMod, 0).
        /// The 0 lower bound is intentionally hardcoded, not configurable -
        /// vanilla behavior tables (milking rejection %, aggression/flee
        /// thresholds) are built around that range.
        /// </summary>
        public void RecomputeEffectiveGeneration()
        {
            int effective = (int)GameMath.Max(Bloodline + FeedMod + InteractMod, 0);
            entity.WatchedAttributes.SetInt("generation", effective);
        }
    }
}
