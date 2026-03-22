using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using j_red.Patches;

namespace j_red
{
    public class ModConfig
    {
        public ConfigEntry<bool> headBobbing;
        public ConfigEntry<bool> toggleSprint;
        // public ConfigEntry<bool> lockFOV;
    }

    [BepInPlugin(GUID, ModName, ModVersion)]
    public class ModBase : BaseUnityPlugin
    {
        private const string GUID = "asta.HqExtra";
        private const string ModName = "HqExtra";
        private const string ModVersion = "1.0.0";

        private readonly Harmony harmony = new Harmony(GUID);

        private static ModBase Instance;
        public static ModConfig config;

        internal ManualLogSource logger;

        internal static ManualLogSource Log => Instance?.logger;

        void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                config = new ModConfig();

                config.toggleSprint = Config.Bind("General", "Toggle Sprint", false, "If sprinting should toggle on key press instead of requiring the key to be held.");
                config.headBobbing = Config.Bind("General", "Head Bobbing", true, "If head bobbing should be enabled.");
                // config.lockFOV = Config.Bind("General", "Lock FOV", true, "Determines if the player field of view should remain locked. Disable for mod compatibility.");
            }

            logger = BepInEx.Logging.Logger.CreateLogSource(GUID);
            logger.LogInfo("HqExtra v" + ModVersion + " initialized.");

            harmony.PatchAll();
        }
    }
}
