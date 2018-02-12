using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace PickUpAndHaul
{
    [StaticConstructorOnStartup]
    public class ModCompatibilityCheck
    {
        public static bool CombatExtendedIsActive
        {
            get
            {
                return ModsConfig.ActiveModsInLoadOrder.Any(m => m.Name == "Combat Extended" /*|| m.Name == "While You're Up"*/);
            }
        }

        public static bool SimplesidearmsIsActive
        {
            get
            {
                return ModsConfig.ActiveModsInLoadOrder.Any(m => m.Name == "Simple sidearms");
            }
        }

        public static bool ExtendedStorageIsActive
        {
            get
            {
                return ModsConfig.ActiveModsInLoadOrder.Any(m => m.Name == "ExtendedStorageFluffyHarmonised");
            }
        }
    }
}
