using System;
using System.Collections.Generic;
using UnityEngine;

namespace Extra.TelemetryLog
{
    [DisallowMultipleComponent]
    public class GeneralFeatureLogManager : MonoBehaviour, ITelemetryProvider
    {
        private static GeneralFeatureLogManager _instance;

        [Header("Entity Scanning Tags")]
        [Tooltip("Tag used to locate the local player.")]
        [SerializeField] private string playerTag = "Player";

        [Tooltip("Tag used to locate enemies.")]
        [SerializeField] private string enemyTag = "Enemy";

        [Tooltip("Tag used to locate interactable environmental objects.")]
        [SerializeField] private string interactableTag = "Interactable";

        // External delegates for game integration
        public Func<GameObject, float> BotHealthProvider { get; set; }
        public Func<GameObject, float> BotSpeedProvider { get; set; }
        public Func<float> ScoreProvider { get; set; }

        // Input collector instance
        private readonly TelemetryInputCollector _inputCollector = new TelemetryInputCollector();

        // Player tracking
        private Transform _playerTransform;
        private Vector3 _lastPlayerPos;
        private float _windowPlayerMovement;

        // Bot tracking
        private readonly Dictionary<int, Vector3> _lastBotPositions = new Dictionary<int, Vector3>();
        private float _windowBotMovement;

        // Event tracking
        private readonly List<string> _eventAccumulator = new List<string>();
        private readonly object _eventLock = new object();

        // Warning state to prevent console spam
        private bool _warnedPlayerTag;
        private bool _warnedEnemyTag;
        private bool _warnedInteractableTag;

        private void Awake()
        {
            _instance = this;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        /// <summary>
        /// Registers a gameplay event. Safe to call from any thread or script.
        /// </summary>
        public static void LogEvent(string eventType)
        {
            if (string.IsNullOrEmpty(eventType)) return;
            if (_instance != null)
            {
                lock (_instance._eventLock)
                {
                    _instance._eventAccumulator.Add(eventType);
                }
            }
        }

        public string[] GetHeaders()
        {
            return new string[]
            {
                "input_intensity",
                "input_diversity",
                "idle_time",
                "activity",
                "movement",
                "score",
                "bot_count",
                "bot_movement",
                "bot_diversity",
                "avg_bot_health",
                "avg_bot_speed",
                "avg_bot_distance",
                "object_intensity",
                "object_diversity",
                "event_intensity",
                "event_diversity"
            };
        }

        public void CollectFrameData()
        {
            // 1. Collect inputs
            _inputCollector.CollectFrame();

            // 2. Accumulate player movement
            if (_playerTransform == null && !string.IsNullOrEmpty(playerTag))
            {
                try
                {
                    GameObject playerGo = GameObject.FindWithTag(playerTag);
                    if (playerGo != null)
                    {
                        _playerTransform = playerGo.transform;
                        _lastPlayerPos = _playerTransform.position;
                    }
                }
                catch (UnityException)
                {
                    if (!_warnedPlayerTag)
                    {
                        Debug.LogWarning($"[GeneralFeatureLogManager] Player tag '{playerTag}' is not defined in the project settings.");
                        _warnedPlayerTag = true;
                    }
                }
            }

            if (_playerTransform != null)
            {
                Vector3 currentPos = _playerTransform.position;
                _windowPlayerMovement += Vector3.Distance(currentPos, _lastPlayerPos);
                _lastPlayerPos = currentPos;
            }

            // 3. Accumulate bot movement
            Camera cam = Camera.main;
            if (cam != null && !string.IsNullOrEmpty(enemyTag))
            {
                try
                {
                    GameObject[] enemies = GameObject.FindGameObjectsWithTag(enemyTag);
                    foreach (GameObject enemy in enemies)
                    {
                        if (enemy == null) continue;

                        Renderer r = enemy.GetComponent<Renderer>();
                        bool isVisible = r != null ? FrustumUtils.IsVisibleToCamera(r, cam) : FrustumUtils.IsVisibleToCamera(enemy.transform, cam);

                        if (isVisible)
                        {
                            int id = enemy.GetInstanceID();
                            Vector3 currentPos = enemy.transform.position;
                            if (_lastBotPositions.TryGetValue(id, out Vector3 lastPos))
                            {
                                _windowBotMovement += Vector3.Distance(currentPos, lastPos);
                            }
                            _lastBotPositions[id] = currentPos;
                        }
                    }
                }
                catch (UnityException)
                {
                    if (!_warnedEnemyTag)
                    {
                        Debug.LogWarning($"[GeneralFeatureLogManager] Enemy tag '{enemyTag}' is not defined in the project settings.");
                        _warnedEnemyTag = true;
                    }
                }
            }
        }

        public string[] FlushAndGetSnapshot()
        {
            // Flush input collector
            _inputCollector.Flush();

            // Retrieve camera
            Camera cam = Camera.main;

            // Player score
            float score = ScoreProvider?.Invoke() ?? 0f;

            // Initialize bot and object lists
            int botCount = 0;
            float totalBotHealth = 0f;
            float totalBotSpeed = 0f;
            float totalBotDistance = 0f;
            HashSet<string> uniqueBotTypes = new HashSet<string>();

            int objectCount = 0;
            HashSet<string> uniqueObjectTypes = new HashSet<string>();

            if (cam != null)
            {
                // A. Spatial Gated Bots
                if (!string.IsNullOrEmpty(enemyTag))
                {
                    try
                    {
                        GameObject[] enemies = GameObject.FindGameObjectsWithTag(enemyTag);
                        foreach (GameObject enemy in enemies)
                        {
                            if (enemy == null) continue;

                            Renderer r = enemy.GetComponent<Renderer>();
                            bool isVisible = r != null ? FrustumUtils.IsVisibleToCamera(r, cam) : FrustumUtils.IsVisibleToCamera(enemy.transform, cam);

                            if (isVisible)
                            {
                                botCount++;
                                
                                // Clean name for diversity
                                string typeName = enemy.name;
                                int idx = typeName.IndexOf('(');
                                if (idx > 0) typeName = typeName.Substring(0, idx).Trim();
                                uniqueBotTypes.Add(typeName);

                                // Health
                                totalBotHealth += BotHealthProvider?.Invoke(enemy) ?? 0f;

                                // Speed
                                if (BotSpeedProvider != null)
                                {
                                    totalBotSpeed += BotSpeedProvider(enemy);
                                }
                                else
                                {
                                    Rigidbody rb = enemy.GetComponent<Rigidbody>();
                                    if (rb != null) totalBotSpeed += rb.velocity.magnitude;
                                    else
                                    {
                                        Rigidbody2D rb2d = enemy.GetComponent<Rigidbody2D>();
                                        if (rb2d != null) totalBotSpeed += rb2d.velocity.magnitude;
                                    }
                                }

                                // Distance to player
                                if (_playerTransform != null)
                                {
                                    totalBotDistance += Vector3.Distance(_playerTransform.position, enemy.transform.position);
                                }
                            }
                        }
                    }
                    catch (UnityException)
                    {
                        // Already warned in CollectFrameData
                    }
                }

                // B. Spatial Gated Environment Objects
                if (!string.IsNullOrEmpty(interactableTag))
                {
                    try
                    {
                        GameObject[] interactables = GameObject.FindGameObjectsWithTag(interactableTag);
                        foreach (GameObject obj in interactables)
                        {
                            if (obj == null) continue;

                            Renderer r = obj.GetComponent<Renderer>();
                            bool isVisible = r != null ? FrustumUtils.IsVisibleToCamera(r, cam) : FrustumUtils.IsVisibleToCamera(obj.transform, cam);

                            if (isVisible)
                            {
                                objectCount++;
                                string typeName = obj.name;
                                int idx = typeName.IndexOf('(');
                                if (idx > 0) typeName = typeName.Substring(0, idx).Trim();
                                uniqueObjectTypes.Add(typeName);
                            }
                        }
                    }
                    catch (UnityException)
                    {
                        if (!_warnedInteractableTag)
                        {
                            Debug.LogWarning($"[GeneralFeatureLogManager] Interactable tag '{interactableTag}' is not defined in the project settings.");
                            _warnedInteractableTag = true;
                        }
                    }
                }
            }

            // Events
            int eventIntensity = 0;
            int eventDiversity = 0;
            lock (_eventLock)
            {
                eventIntensity = _eventAccumulator.Count;
                HashSet<string> uniqueEvents = new HashSet<string>(_eventAccumulator);
                eventDiversity = uniqueEvents.Count;
                _eventAccumulator.Clear();
            }

            // Computations
            float avgHealth = botCount > 0 ? totalBotHealth / botCount : 0f;
            float avgSpeed = botCount > 0 ? totalBotSpeed / botCount : 0f;
            float avgDistance = botCount > 0 ? totalBotDistance / botCount : 0f;

            // Snapshot values
            string[] snapshot = new string[16];
            snapshot[0] = _inputCollector.InputIntensity.ToString();
            snapshot[1] = _inputCollector.InputDiversity.ToString();
            snapshot[2] = _inputCollector.IdleFraction.ToString("F3");
            snapshot[3] = (1f - _inputCollector.IdleFraction).ToString("F3");
            snapshot[4] = _windowPlayerMovement.ToString("F3");
            snapshot[5] = score.ToString("F3");
            snapshot[6] = botCount.ToString();
            snapshot[7] = _windowBotMovement.ToString("F3");
            snapshot[8] = uniqueBotTypes.Count.ToString();
            snapshot[9] = avgHealth.ToString("F3");
            snapshot[10] = avgSpeed.ToString("F3");
            snapshot[11] = avgDistance.ToString("F3");
            snapshot[12] = objectCount.ToString();
            snapshot[13] = uniqueObjectTypes.Count.ToString();
            snapshot[14] = eventIntensity.ToString();
            snapshot[15] = eventDiversity.ToString();

            // Reset accumulators
            _windowPlayerMovement = 0f;
            _windowBotMovement = 0f;
            _lastBotPositions.Clear();

            // Reset last player position in case player moved
            if (_playerTransform != null)
            {
                _lastPlayerPos = _playerTransform.position;
            }

            return snapshot;
        }
    }
}
