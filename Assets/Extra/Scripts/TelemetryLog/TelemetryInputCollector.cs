using System.Collections.Generic;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Extra.TelemetryLog
{
    public class TelemetryInputCollector
    {
        // Flushed values (snapshot of the last window)
        public int InputIntensity { get; private set; }
        public int InputDiversity { get; private set; }
        public float IdleFraction { get; private set; }

        // Accumulators for the active window
        private int _windowIntensity;
        private readonly HashSet<string> _windowDiversity = new HashSet<string>();
        private int _totalFramesInWindow;
        private int _idleFramesInWindow;

        // Temporary per-frame collections to avoid allocations
        private readonly HashSet<string> _frameDiversity = new HashSet<string>();

        /// <summary>
        /// Collects input metrics for the current frame. Call this from Update.
        /// </summary>
        public void CollectFrame()
        {
            _frameDiversity.Clear();
            int frameIntensity = 0;

#if ENABLE_INPUT_SYSTEM
            // New Input System
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.anyKey.wasPressedThisFrame)
                {
                    var allKeys = keyboard.allKeys;
                    for (int i = 0; i < allKeys.Count; i++)
                    {
                        if (allKeys[i].wasPressedThisFrame)
                        {
                            frameIntensity++;
                            _frameDiversity.Add($"key_{allKeys[i].name}");
                        }
                    }
                }

                // Check driving keys hold state
                Key[] driveKeys = new Key[] { Key.W, Key.A, Key.S, Key.D, Key.UpArrow, Key.DownArrow, Key.LeftArrow, Key.RightArrow };
                foreach (Key key in driveKeys)
                {
                    if (keyboard[key].isPressed)
                    {
                        _frameDiversity.Add($"key_hold_{key}");
                    }
                }
            }

            // Check Mouse
            var mouse = Mouse.current;
            if (mouse != null)
            {
                if (mouse.leftButton.wasPressedThisFrame) { frameIntensity++; _frameDiversity.Add("mouse_down_0"); }
                else if (mouse.leftButton.isPressed) { _frameDiversity.Add("mouse_hold_0"); }

                if (mouse.rightButton.wasPressedThisFrame) { frameIntensity++; _frameDiversity.Add("mouse_down_1"); }
                else if (mouse.rightButton.isPressed) { _frameDiversity.Add("mouse_hold_1"); }

                if (mouse.middleButton.wasPressedThisFrame) { frameIntensity++; _frameDiversity.Add("mouse_down_2"); }
                else if (mouse.middleButton.isPressed) { _frameDiversity.Add("mouse_hold_2"); }
            }

            // Check Touches
            var touchscreen = Touchscreen.current;
            if (touchscreen != null)
            {
                var touches = touchscreen.touches;
                for (int i = 0; i < touches.Count; i++)
                {
                    var touchControl = touches[i];
                    if (touchControl.press.wasPressedThisFrame)
                    {
                        frameIntensity++;
                        _frameDiversity.Add("touch_tap");
                    }
                    else if (touchControl.press.isPressed)
                    {
                        if (touchControl.delta.ReadValue().sqrMagnitude > 0.1f)
                        {
                            _frameDiversity.Add("touch_drag");
                        }
                        else
                        {
                            _frameDiversity.Add("touch_stationary");
                        }
                    }
                }
            }
#else
            // Legacy Mouse & Keyboard / Touch Input
            #if UNITY_IOS || UNITY_ANDROID
            int touchCount = Input.touchCount;
            if (touchCount > 0)
            {
                for (int i = 0; i < touchCount; i++)
                {
                    Touch touch = Input.GetTouch(i);
                    if (touch.phase == TouchPhase.Began)
                    {
                        frameIntensity++;
                        _frameDiversity.Add("touch_tap");
                    }
                    else if (touch.phase == TouchPhase.Moved)
                    {
                        _frameDiversity.Add("touch_drag");
                    }
                    else if (touch.phase == TouchPhase.Stationary)
                    {
                        _frameDiversity.Add("touch_stationary");
                    }
                }
            }
            #else
            for (int i = 0; i < 3; i++)
            {
                if (Input.GetMouseButtonDown(i))
                {
                    frameIntensity++;
                    _frameDiversity.Add($"mouse_down_{i}");
                }
                else if (Input.GetMouseButton(i))
                {
                    _frameDiversity.Add($"mouse_hold_{i}");
                }
            }

            if (Input.anyKeyDown)
            {
                string keys = Input.inputString;
                if (!string.IsNullOrEmpty(keys))
                {
                    foreach (char c in keys)
                    {
                        frameIntensity++;
                        _frameDiversity.Add($"key_{c}");
                    }
                }
                else
                {
                    // Fallback for control/arrow keys
                    KeyCode[] checkKeys = new KeyCode[] {
                        KeyCode.Space, KeyCode.LeftShift, KeyCode.RightShift, KeyCode.Escape, KeyCode.Return,
                        KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow,
                        KeyCode.W, KeyCode.A, KeyCode.S, KeyCode.D
                    };
                    foreach (KeyCode key in checkKeys)
                    {
                        if (Input.GetKeyDown(key))
                        {
                            frameIntensity++;
                            _frameDiversity.Add($"key_{key}");
                        }
                    }
                }
            }

            // Also capture continuous keyboard inputs (e.g. WASD held down for driving)
            KeyCode[] driveKeys = new KeyCode[] { KeyCode.W, KeyCode.A, KeyCode.S, KeyCode.D, KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow };
            foreach (KeyCode key in driveKeys)
            {
                if (Input.GetKey(key))
                {
                    _frameDiversity.Add($"key_hold_{key}");
                }
            }
            #endif
#endif

            // Accumulate frame metrics
            _windowIntensity += frameIntensity;
            foreach (string val in _frameDiversity)
            {
                _windowDiversity.Add(val);
            }

            _totalFramesInWindow++;
            if (frameIntensity == 0 && _frameDiversity.Count == 0)
            {
                _idleFramesInWindow++;
            }
        }

        /// <summary>
        /// Computes final values for the active window and resets accumulators.
        /// </summary>
        public void Flush()
        {
            InputIntensity = _windowIntensity;
            InputDiversity = _windowDiversity.Count;
            IdleFraction = _totalFramesInWindow > 0 ? (float)_idleFramesInWindow / _totalFramesInWindow : 1.0f;

            // Reset accumulators
            _windowIntensity = 0;
            _windowDiversity.Clear();
            _totalFramesInWindow = 0;
            _idleFramesInWindow = 0;
        }
    }
}

