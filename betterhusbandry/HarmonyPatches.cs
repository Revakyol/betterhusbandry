using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;
using Vintagestory.API.MathTools;

namespace betterhusbandry
{
    /// <summary>
    /// IMPORTANT: the method names/signatures targeted below are best guesses
    /// based on typical VS naming conventions, NOT confirmed against the
    /// actual VSSurvivalMod.dll for your installed version. Before this will
    /// compile/work, open the DLL in ILSpy/dnSpy and confirm:
    ///
    ///   1. Where EntityBehaviorMultiply assigns a newborn's "generation"
    ///      WatchedAttribute from the mother (search for the string
    ///      "generation" inside EntityBehaviorMultiply).
    ///   2. Where the milking interaction reads "generation" to compute
    ///      rejection chance (search for "generation" again, likely in a
    ///      milking-specific behavior or the bucket's CollectibleBehavior -
    ///      this is probably the fastest of the three to find since it's a
    ///      confirmed, direct generation read).
    ///   3. Where the pet interaction is handled (search for the
    ///      generation >= 1 gate mentioned on the wiki).
    ///   4. Where trough feeding actually lands (likely inside whatever
    ///      implements "animalhunger" / portion consumption).
    ///
    /// Once you've got real method signatures, swap the commented
    /// [HarmonyPatch] stubs below for the confirmed ones.
    /// </summary>
    public static class HarmonyPatches
    {
        /// <summary>
        /// TODO: confirm target method for successful (or attempted -
        /// your call whether attempts short of success still count) milking.
        /// </summary>
        [HarmonyPatch(typeof(EntityBehaviorMilkable), "MilkingComplete")]
        public static class Patch_OnMilk
        {
            [HarmonyPostfix]
            public static void Postfix(ItemSlot slot, Entity byEntity, Entity ___entity)
            {
                if(!(byEntity is EntityPlayer)) return;
                if(___entity.World.Side != EnumAppSide.Server) return;

                var behavior = ___entity.GetBehavior<EntityBehaviorbetterhusbandry>();
                behavior?.RegisterInteract(byEntity.World.Calendar.TotalHours);
            }
        }

        /// <summary>
        /// TODO: confirm target method for the pet interaction.
        /// </summary>
        [HarmonyPatch(typeof(EntityBehaviorPettable), "OnInteract")]
        public static class Patch_OnPet
        {
            [HarmonyPostfix]
            public static void Postfix(EntityAgent byEntity, Entity ___entity, float ___petDurationS)
            {
                //Mirror vanilla's Trigger conditions for petting
                if(!(byEntity is EntityPlayer)) return;
                if(___petDurationS < 0.6) return;
                if(___entity.World.Side != EnumAppSide.Server) return;

                //We are now petting, and should log it
                var behavior = ___entity.GetBehavior<EntityBehaviorbetterhusbandry>();
                behavior?.RegisterInteract(byEntity.World.Calendar.TotalHours);
            }
        }

        [HarmonyPatch(typeof(EntityBehaviorMultiply), "GiveBirth")]
        public static class Patch_GiveBirth
        {
            // Static because GiveBirth reads mother's "generation" once, uses it for
            // every child in its own loop, and calling code is effectively
            // single-threaded on the main game loop - reentrancy isn't a practical
            // concern here, but flagging it in case that assumption ever changes.
            static int savedGeneration;

            [HarmonyPrefix]
            public static void Prefix(Entity ___entity)
            {
                var motherBehavior = ___entity.GetBehavior<EntityBehaviorbetterhusbandry>();
                if (motherBehavior == null) return;

                savedGeneration = ___entity.WatchedAttributes.GetInt("generation", 0);

                // Temporarily present Bloodline (not the live, care-affected
                // generation) as "generation", so GiveBirth stamps bloodline+1 onto
                // each newborn - EntityBehaviorbetterhusbandry.Initialize already
                // reads that value correctly, no change needed there.
                ___entity.WatchedAttributes.SetInt("generation", motherBehavior.Bloodline);
            }

            [HarmonyPostfix]
            public static void Postfix(Entity ___entity)
            {
                if (___entity.GetBehavior<EntityBehaviorbetterhusbandry>() == null) return;

                ___entity.WatchedAttributes.SetInt("generation", savedGeneration);
            }
        }
    }
}
