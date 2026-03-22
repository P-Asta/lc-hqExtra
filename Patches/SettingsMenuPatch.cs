using BepInEx.Configuration;
using HarmonyLib;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace j_red.Patches
{
    [HarmonyPatch]
    internal static class SettingsMenuPatch
    {
        private const string ClonePrefix = "Accessibility_";
        private const float VerticalSpacing = 25f;
        private static bool hasLoggedSuccessfulInjection;
        private static bool hasLoggedMissingTemplate;
        private static readonly Dictionary<ConfigEntry<bool>, bool> PendingValues = new Dictionary<ConfigEntry<bool>, bool>();

        [HarmonyPatch(typeof(QuickMenuManager), "OpenQuickMenu")]
        [HarmonyPostfix]
        private static void InjectSettingsOnQuickMenuOpen(QuickMenuManager __instance)
        {
            ModBase.Log?.LogInfo("QuickMenuManager.OpenQuickMenu called.");
            EnsureInjected(__instance != null ? __instance.settingsPanel?.transform : null, "QuickMenu");
        }

        [HarmonyPatch(typeof(MenuManager), "EnableUIPanel")]
        [HarmonyPostfix]
        private static void InjectSettingsOnEnablePanel(GameObject enablePanel)
        {
            if (enablePanel == null)
            {
                return;
            }

            ModBase.Log?.LogInfo("MenuManager.EnableUIPanel called for: " + enablePanel.name);
            if (enablePanel.name == "SettingsPanel")
            {
                EnsureInjected(enablePanel.transform, "MainMenu");
            }
        }

        [HarmonyPatch(typeof(IngamePlayerSettings), "SaveChangedSettings")]
        [HarmonyPostfix]
        private static void ApplyPendingSettingsOnConfirm()
        {
            ApplyPendingValues();
        }

        [HarmonyPatch(typeof(IngamePlayerSettings), "DiscardChangedSettings")]
        [HarmonyPostfix]
        private static void DiscardPendingSettingsOnCancel()
        {
            DiscardPendingValues();
        }

        internal static void EnsureInjected(Transform settingsPanelRoot, string source)
        {
            if (ModBase.config == null || settingsPanelRoot == null)
            {
                return;
            }

            GameObject original = FindTemplateToggle(settingsPanelRoot);
            if (original == null)
            {
                if (!hasLoggedMissingTemplate)
                {
                    ModBase.Log?.LogInfo("ControlsOptions template toggle not found in " + source + " settings panel.");
                    DumpControlsOptionsCandidates(settingsPanelRoot);
                    hasLoggedMissingTemplate = true;
                }
                return;
            }

            hasLoggedMissingTemplate = false;

            Transform parent = original.transform.parent;
            if (parent == null)
            {
                return;
            }

            LayoutAnchor anchor = GetOrCreateLayoutAnchor(original);
            float originalY = anchor.BaseAnchoredY;
            int originalSiblingIndex = anchor.BaseSiblingIndex;

            CreateOrRefreshToggle(parent, original, "ToggleSprint", "Toggle Sprint", ModBase.config.toggleSprint, originalY, originalSiblingIndex);
            CreateOrRefreshToggle(parent, original, "HeadBobbing", "Head Bobbing", ModBase.config.headBobbing, originalY - VerticalSpacing, originalSiblingIndex + 1);
            RepositionOriginal(original, originalY - (2f * VerticalSpacing), originalSiblingIndex + 2);
            // CreateOrRefreshToggle(parent, original, "LockFOV", "Lock FOV", ModBase.config.lockFOV, 2);
            RemoveInjectedToggle(parent, "LockFOV");
            RemoveInjectedToggle(parent, "DisableMotionSway");

            if (parent is RectTransform rectTransform)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);
            }

            if (!hasLoggedSuccessfulInjection)
            {
                ModBase.Log?.LogInfo("Accessibility toggles injected under ControlsOptions from " + source + ".");
                hasLoggedSuccessfulInjection = true;
            }
        }

        private static GameObject FindTemplateToggle(Transform settingsPanelRoot)
        {
            Transform[] allTransforms = settingsPanelRoot.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < allTransforms.Length; i++)
            {
                Transform transform = allTransforms[i];
                if (transform == null)
                {
                    continue;
                }

                if (transform.name != "ControlsOptions")
                {
                    continue;
                }

                GameObject preferred = FindPreferredTemplate(transform);
                if (preferred != null)
                {
                    return preferred;
                }
            }

            return null;
        }

        private static GameObject FindPreferredTemplate(Transform controlsOptions)
        {
            GameObject fallback = null;

            for (int i = 0; i < controlsOptions.childCount; i++)
            {
                Transform child = controlsOptions.GetChild(i);
                if (child == null)
                {
                    continue;
                }

                SettingsOption option = child.GetComponent<SettingsOption>();
                TextMeshProUGUI text = child.GetComponentInChildren<TextMeshProUGUI>(true);
                if (option == null || text == null)
                {
                    continue;
                }

                if (fallback == null)
                {
                    fallback = child.gameObject;
                }

                string childName = child.name ?? string.Empty;
                string childText = text.text ?? string.Empty;
                if (childName.Contains("Arachn") || childText.Contains("Arachn"))
                {
                    return child.gameObject;
                }
            }

            return fallback;
        }

        private static void DumpControlsOptionsCandidates(Transform settingsPanelRoot)
        {
            Transform[] allTransforms = settingsPanelRoot.GetComponentsInChildren<Transform>(true);
            int controlsOptionsCount = 0;

            for (int i = 0; i < allTransforms.Length; i++)
            {
                Transform transform = allTransforms[i];
                if (transform == null || transform.name != "ControlsOptions")
                {
                    continue;
                }

                controlsOptionsCount++;
                ModBase.Log?.LogInfo("Found ControlsOptions: " + GetTransformPath(transform));

                for (int childIndex = 0; childIndex < transform.childCount; childIndex++)
                {
                    Transform child = transform.GetChild(childIndex);
                    TextMeshProUGUI text = child != null ? child.GetComponentInChildren<TextMeshProUGUI>(true) : null;
                    SettingsOption option = child != null ? child.GetComponent<SettingsOption>() : null;
                    string textValue = text != null ? text.text : "<no text>";
                    ModBase.Log?.LogInfo("ControlsOptions child: " + child?.name + " | text=" + textValue + " | settingsOption=" + (option != null));
                }
            }

            ModBase.Log?.LogInfo("ControlsOptions count: " + controlsOptionsCount);
        }

        private static string GetTransformPath(Transform transform)
        {
            string path = transform.name;
            Transform current = transform.parent;

            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }

        private static void CreateOrRefreshToggle(Transform parent, GameObject original, string id, string label, ConfigEntry<bool> entry, float targetY, int targetSiblingIndex)
        {
            string objectName = ClonePrefix + id;
            Transform existing = parent.Find(objectName);
            if (existing != null)
            {
                ModSettingsToggle existingToggle = existing.GetComponent<ModSettingsToggle>();
                if (existingToggle != null)
                {
                    existingToggle.Refresh();
                }

                UpdatePosition(existing.gameObject, original, targetY);
                existing.SetSiblingIndex(Mathf.Max(0, targetSiblingIndex));
                return;
            }

            GameObject clone = Object.Instantiate(original, parent);
            clone.name = objectName;

            SetLabel(clone, label);
            ReplaceToggleBehaviour(clone, entry, label);
            UpdatePosition(clone, original, targetY);
            clone.transform.SetSiblingIndex(Mathf.Max(0, targetSiblingIndex));

            ModBase.Log?.LogInfo("Added settings toggle: " + objectName);
        }

        private static void RemoveInjectedToggle(Transform parent, string id)
        {
            Transform existing = parent.Find(ClonePrefix + id);
            if (existing != null)
            {
                Object.Destroy(existing.gameObject);
            }
        }

        private static void SetLabel(GameObject clone, string label)
        {
            TextMeshProUGUI[] texts = clone.GetComponentsInChildren<TextMeshProUGUI>(true);
            if (texts != null && texts.Length > 0)
            {
                texts[0].text = label;
            }
        }

        private static void ReplaceToggleBehaviour(GameObject clone, ConfigEntry<bool> entry, string label)
        {
            SettingsOption templateOption = clone.GetComponent<SettingsOption>();
            TMP_Text labelText = templateOption != null ? templateOption.textElement : clone.GetComponentInChildren<TextMeshProUGUI>(true);
            Image toggleImage = templateOption != null ? templateOption.toggleImage : FindBestToggleImage(clone);
            Sprite enabledSprite = templateOption != null ? templateOption.enabledImage : null;
            Sprite disabledSprite = templateOption != null ? templateOption.disabledImage : null;

            foreach (SettingsOption option in clone.GetComponentsInChildren<SettingsOption>(true))
            {
                Object.DestroyImmediate(option);
            }

            if (toggleImage == null)
            {
                ModBase.Log?.LogWarning("Toggle image not found for " + label);
                return;
            }

            ModSettingsToggle settingsToggle = clone.GetComponent<ModSettingsToggle>();
            if (settingsToggle == null)
            {
                settingsToggle = clone.AddComponent<ModSettingsToggle>();
            }

            settingsToggle.Initialize(entry, label, labelText, toggleImage, enabledSprite, disabledSprite);
            settingsToggle.Refresh();
        }

        private static Image FindBestToggleImage(GameObject clone)
        {
            Image[] images = clone.GetComponentsInChildren<Image>(true);
            for (int i = images.Length - 1; i >= 0; i--)
            {
                Image image = images[i];
                if (image != null && image.gameObject != clone)
                {
                    return image;
                }
            }

            return clone.GetComponentInChildren<Image>(true);
        }

        internal static bool GetCurrentValue(ConfigEntry<bool> entry)
        {
            if (entry == null)
            {
                return false;
            }

            return PendingValues.TryGetValue(entry, out bool pendingValue) ? pendingValue : entry.Value;
        }

        internal static void TogglePendingValue(ConfigEntry<bool> entry)
        {
            if (entry == null)
            {
                return;
            }

            bool newValue = !GetCurrentValue(entry);
            PendingValues[entry] = newValue;
            MarkSettingsDirty();
            RefreshAllInjectedToggles();
        }

        private static void ApplyPendingValues()
        {
            if (PendingValues.Count == 0)
            {
                return;
            }

            foreach (KeyValuePair<ConfigEntry<bool>, bool> pendingEntry in PendingValues)
            {
                pendingEntry.Key.Value = pendingEntry.Value;
                ModBase.Log?.LogInfo("Applied setting on confirm: " + pendingEntry.Key.Definition.Key + " -> " + pendingEntry.Value);
            }

            PendingValues.Clear();
            RefreshAllInjectedToggles();
        }

        private static void DiscardPendingValues()
        {
            if (PendingValues.Count == 0)
            {
                return;
            }

            ModBase.Log?.LogInfo("Discarded pending settings changes.");
            PendingValues.Clear();
            RefreshAllInjectedToggles();
        }

        private static void MarkSettingsDirty()
        {
            IngamePlayerSettings ingameSettings = IngamePlayerSettings.Instance;
            if (ingameSettings == null)
            {
                return;
            }

            Traverse.Create(ingameSettings).Field("changesNotApplied").SetValue(true);
            AccessTools.Method(typeof(IngamePlayerSettings), "SetChangesNotAppliedTextVisible")?.Invoke(ingameSettings, new object[] { true });
        }

        private static void RefreshAllInjectedToggles()
        {
            ModSettingsToggle[] toggles = Object.FindObjectsOfType<ModSettingsToggle>(true);
            for (int i = 0; i < toggles.Length; i++)
            {
                toggles[i].Refresh();
            }
        }

        private static void RepositionOriginal(GameObject original, float targetY, int targetSiblingIndex)
        {
            RectTransform originalRect = original.GetComponent<RectTransform>();
            if (originalRect == null)
            {
                return;
            }

            originalRect.anchoredPosition = new Vector2(
                originalRect.anchoredPosition.x,
                targetY
            );
            original.transform.SetSiblingIndex(Mathf.Max(0, targetSiblingIndex));
        }

        private static void UpdatePosition(GameObject clone, GameObject original, float targetY)
        {
            RectTransform cloneRect = clone.GetComponent<RectTransform>();
            RectTransform originalRect = original.GetComponent<RectTransform>();
            if (cloneRect == null || originalRect == null)
            {
                return;
            }

            cloneRect.anchorMin = originalRect.anchorMin;
            cloneRect.anchorMax = originalRect.anchorMax;
            cloneRect.pivot = originalRect.pivot;
            cloneRect.sizeDelta = originalRect.sizeDelta;
            cloneRect.anchoredPosition = new Vector2(
                originalRect.anchoredPosition.x,
                targetY
            );
        }

        private static LayoutAnchor GetOrCreateLayoutAnchor(GameObject original)
        {
            LayoutAnchor anchor = original.GetComponent<LayoutAnchor>();
            if (anchor != null)
            {
                return anchor;
            }

            RectTransform originalRect = original.GetComponent<RectTransform>();
            anchor = original.AddComponent<LayoutAnchor>();
            anchor.BaseAnchoredY = originalRect != null ? originalRect.anchoredPosition.y : 0f;
            anchor.BaseSiblingIndex = original.transform.GetSiblingIndex();
            return anchor;
        }
    }

    internal sealed class ModSettingsToggle : MonoBehaviour, IPointerClickHandler, ISubmitHandler
    {
        private ConfigEntry<bool> configEntry;
        private string label;
        private TMP_Text labelText;
        private Image toggleImage;
        private Sprite enabledSprite;
        private Sprite disabledSprite;

        internal void Initialize(ConfigEntry<bool> entry, string labelValue, TMP_Text text, Image image, Sprite enabled, Sprite disabled)
        {
            configEntry = entry;
            label = labelValue;
            labelText = text;
            toggleImage = image;
            enabledSprite = enabled;
            disabledSprite = disabled;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            ToggleValue();
        }

        public void OnSubmit(BaseEventData eventData)
        {
            ToggleValue();
        }

        private void ToggleValue()
        {
            if (configEntry == null)
            {
                return;
            }

            SettingsMenuPatch.TogglePendingValue(configEntry);
            ModBase.Log?.LogInfo(label + " pending -> " + SettingsMenuPatch.GetCurrentValue(configEntry));
        }

        internal void Refresh()
        {
            if (configEntry == null)
            {
                return;
            }

            if (labelText != null)
            {
                labelText.text = label;
            }

            if (toggleImage != null)
            {
                bool currentValue = SettingsMenuPatch.GetCurrentValue(configEntry);
                toggleImage.sprite = currentValue ? enabledSprite : disabledSprite;
            }
        }

        private void OnEnable()
        {
            Refresh();
        }
    }

    internal sealed class LayoutAnchor : MonoBehaviour
    {
        internal float BaseAnchoredY;
        internal int BaseSiblingIndex;
    }

}
