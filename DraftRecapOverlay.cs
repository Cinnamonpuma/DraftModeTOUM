using UnityEngine;
using TMPro;
using System.Collections.Generic;
using DraftModeTOUM.Managers;
using Reactor.Utilities.Attributes;

namespace DraftModeTOUM
{
    public sealed class RecapEntry
    {
        public int SlotNumber { get; }
        public string RoleName { get; }

        public RecapEntry(int slot, string role)
        {
            SlotNumber = slot;
            RoleName = role;
        }
    }

    [RegisterInIl2Cpp]
    public class DraftRecapOverlay : MonoBehaviour
    {
        private static DraftRecapOverlay? _instance;

        public DraftRecapOverlay(System.IntPtr ptr) : base(ptr) { }

        public static void Show(List<RecapEntry> entries)
        {
            Hide();
            var go = new GameObject("DraftRecapOverlay");

            // Attach directly to the HUD so it layers correctly
            if (HudManager.Instance != null)
            {
                go.transform.SetParent(HudManager.Instance.transform, false);
                go.transform.localPosition = new Vector3(0, 0, -25f);
            }
            else
            {
                DontDestroyOnLoad(go);
            }

            _instance = go.AddComponent<DraftRecapOverlay>();
            _instance.BuildUI(entries);
        }

        public static void Hide()
        {
            // 1. Destroy the main instance
            if (_instance != null)
            {
                try
                {
                    if (_instance.gameObject != null)
                    {
                        _instance.gameObject.SetActive(false); // Instantly vanish visually
                        Destroy(_instance.gameObject);
                    }
                }
                catch { }
                _instance = null;
            }

            // 2. Aggressive fallback: seek and destroy
            var leftover = GameObject.Find("DraftRecapOverlay");
            if (leftover != null)
            {
                leftover.SetActive(false);
                Destroy(leftover);
            }

            // 3. Clean up the orphaned root from the previous buggy code if it's stuck in memory
            var oldRoot = GameObject.Find("RecapRoot");
            if (oldRoot != null)
            {
                oldRoot.SetActive(false);
                Destroy(oldRoot);
            }
        }

        private void BuildUI(List<RecapEntry> entries)
        {
            if (HudManager.Instance == null) return;

            var font = HudManager.Instance.TaskPanel.taskText.font;
            var fontMat = HudManager.Instance.TaskPanel.taskText.fontMaterial;

            // Notice we parent directly to "gameObject" now, not a separate root!
            var title = MakeText(gameObject, "Title", font, fontMat, 2.5f, new Vector3(0, 2.4f, 0));
            title.text = "<color=#FFD700><b>── DRAFT RECAP ──</b></color>";

            int count = entries.Count;
            int cols = count > 8 ? 2 : 1;
            int perCol = Mathf.CeilToInt(count / (float)cols);

            float startY = 1.4f;
            float ySpacing = 0.45f;
            float xOffset = cols == 2 ? 3.0f : 0f;

            for (int i = 0; i < count; i++)
            {
                var entry = entries[i];
                int col = i / perCol;
                int row = i % perCol;

                float x = (col == 0 && cols == 2) ? -xOffset : (col == 1 ? xOffset : 0f);
                float y = startY - (row * ySpacing);

                var txt = MakeText(gameObject, $"Entry_{i}", font, fontMat, 1.8f, new Vector3(x, y, 0));

                Color c = RoleColors.GetColor(entry.RoleName);
                string colorHex = ColorUtility.ToHtmlStringRGB(c);

                txt.text = $"Player {entry.SlotNumber}: <color=#{colorHex}><b>{entry.RoleName}</b></color>";
            }
        }

        private TextMeshPro MakeText(GameObject parent, string name, TMP_FontAsset font, Material fontMat, float size, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = pos;

            var tmp = go.AddComponent(Il2CppInterop.Runtime.Il2CppType.Of<TextMeshPro>()).Cast<TextMeshPro>();
            tmp.font = font;
            tmp.fontMaterial = fontMat;
            tmp.fontSize = size;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;

            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                r.sortingLayerName = "UI";
                r.sortingOrder = 60;
            }
            return tmp;
        }
    }
}
