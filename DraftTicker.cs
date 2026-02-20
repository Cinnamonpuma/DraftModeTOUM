using DraftModeTOUM.Managers;
using UnityEngine;

namespace DraftModeTOUM
{
    public class DraftTicker : MonoBehaviour
    {
        private static DraftTicker? _instance;

        public static void EnsureExists()
        {
            if (_instance != null) return;
            var go = new GameObject("DraftTicker");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<DraftTicker>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

        private void Update()
        {
            DraftManager.Tick(Time.deltaTime);
        }
    }
}