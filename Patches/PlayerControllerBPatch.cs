using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;

namespace j_red.Patches
{
    [HarmonyPatch(typeof(PlayerControllerB))]
    internal class PlayerControllerBPatch
    {
        public static Camera playerCam = null;
        private static float initialFov;

        [HarmonyPatch("Awake")]
        [HarmonyPostfix]
        static void CacheCameraContainer(ref PlayerControllerB __instance)
        {
            Transform player = __instance.transform;
            playerCam = player.Find("ScavengerModel/metarig/CameraContainer/MainCamera").GetComponent<Camera>();
            initialFov = playerCam.fieldOfView;
        }

        private static readonly Vector3 cameraRotation = new Vector3(90f, 0f, 0f);

        [HarmonyPatch("LateUpdate")]
        [HarmonyPrefix]
        static void LateUpdatePatch(ref PlayerControllerB __instance)
        {
            if (!__instance.inTerminalMenu && !__instance.inSpecialInteractAnimation && !__instance.playingQuickSpecialAnimation)
            {
                if (!ModBase.config.headBobbing.Value)
                {
                    __instance.cameraContainerTransform.position = new Vector3(
                        __instance.cameraContainerTransform.position.x,
                        __instance.playerModelArmsMetarig.transform.position.y,
                        __instance.cameraContainerTransform.position.z
                    );

                    __instance.cameraContainerTransform.localRotation = Quaternion.Euler(cameraRotation);
                }
            }

            // if (ModBase.config.lockFOV.Value && playerCam)
            // {
            //     playerCam.fieldOfView = initialFov;
            // }
        }
    }
}
