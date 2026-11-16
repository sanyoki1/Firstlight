using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Firstlight.Core
{
    /// <summary>
    /// Angled top-down camera. WASD / arrows pan, Q/E rotate, mouse wheel (or PageUp/PageDown, +/-) zooms,
    /// right-drag rotates and tilts, middle-drag pans.
    /// </summary>
    public sealed class OrbitCamera : MonoBehaviour
    {
        public Vector3 Focus;
        public float Yaw = 30f;
        public float Pitch = 52f;
        public float Distance = 40f;
        public float MinDistance = 6f;
        /// <summary>The lowest tilt (degrees above the horizon): low enough to look across the land to the sky.</summary>
        public float MinPitch = 12f;
        public float MaxDistance = 320f;
        public Vector2 BoundsMin = new(0, 0);
        public Vector2 BoundsMax = new(256, 256);
        public Func<float, float, float> HeightAt; // terrain height for the focus point

        /// <summary>Set by the UI each frame when the pointer is over a panel.</summary>
        public static bool PointerOverUI;
        /// <summary>Set by the UI when a text field or modal screen has keyboard focus.</summary>
        public static bool KeyboardCaptured;
        /// <summary>Pan when the pointer touches the screen edge.</summary>
        public bool EdgeScroll = true;
        /// <summary>Player settings: edge scrolling on/off and a multiplier on pan and turn speed.</summary>
        public static bool EdgeScrollEnabled = true;
        public static float SpeedScale = 1f;
        /// <summary>Player setting: the view eases after zooms, turns and jumps (off = it follows at once).</summary>
        public static bool Glide = true;
        /// <summary>No input at all (the main menu's slow orbit, a modal screen).</summary>
        public static bool InputLocked;
        /// <summary>Set when the player pans the camera (keys, screen edge, middle-drag); the HUD's "Follow" stops on it and clears it.</summary>
        public static bool PlayerPanned;

        public Camera Cam { get; private set; }

        void Awake() => Cam = GetComponent<Camera>();

        public void SetView(Vector3 focus, float distance, float pitch, float yaw)
        {
            Focus = focus; Distance = distance; Pitch = pitch; Yaw = yaw;
            _snap = true;
            Apply();
        }

        // What is drawn glides after Focus / Yaw / Pitch / Distance (the values input and the game set), so zoom steps,
        // jumps to an event and key turns ease in and out instead of snapping. SetView cuts straight to a view.
        Vector3 _sFocus; float _sYaw, _sPitch, _sDist;
        bool _snap = true;

        void LateUpdate()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            var kb = Keyboard.current;
            var mouse = Mouse.current;

            Vector2 move = Vector2.zero;
            float rot = 0;
            // Edge scrolling: only with a real, focused window (in batch mode the pointer sits at 0,0).
            if (InputLocked) { Apply(); return; }
            if (mouse != null && EdgeScroll && EdgeScrollEnabled && Application.isFocused && !Application.isBatchMode)
            {
                var mp = mouse.position.ReadValue();
                const float edge = 4f;
                if (mp.x >= 0 && mp.y >= 0 && mp.x <= Screen.width && mp.y <= Screen.height)
                {
                    if (mp.x < edge) move.x -= 1;
                    if (mp.x > Screen.width - edge) move.x += 1;
                    if (mp.y < edge) move.y -= 1;
                    if (mp.y > Screen.height - edge) move.y += 1;
                }
            }
            if (kb != null && !KeyboardCaptured)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) move.y += 1;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) move.y -= 1;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) move.x -= 1;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) move.x += 1;
                if (kb.qKey.isPressed) rot += 1;
                if (kb.eKey.isPressed) rot -= 1;
                // Zoom keys (keyboard-only players had none): PageUp / + in, PageDown / - out, about 8 wheel notches a second.
                float zoom = 0;
                if (kb.pageUpKey.isPressed || kb.equalsKey.isPressed || kb.numpadPlusKey.isPressed) zoom += 1;
                if (kb.pageDownKey.isPressed || kb.minusKey.isPressed || kb.numpadMinusKey.isPressed) zoom -= 1;
                if (zoom != 0) Distance *= Mathf.Pow(0.87f, zoom * 8f * dt);
            }

            float yawRad = Yaw * Mathf.Deg2Rad;
            var fwd = new Vector3(Mathf.Sin(yawRad), 0, Mathf.Cos(yawRad));
            var right = new Vector3(Mathf.Cos(yawRad), 0, -Mathf.Sin(yawRad));
            float panSpeed = Mathf.Max(8f, Distance * 0.9f) * SpeedScale;
            Focus += (right * move.x + fwd * move.y) * (panSpeed * dt);
            if (move != Vector2.zero) PlayerPanned = true;
            Yaw += rot * 90f * dt * SpeedScale;

            if (mouse != null)
            {
                Vector2 delta = mouse.delta.ReadValue();
                if (!PointerOverUI)
                {
                    float scroll = mouse.scroll.ReadValue().y;
                    if (Mathf.Abs(scroll) > 0.01f)
                    {
                        float notches = Mathf.Clamp(scroll / 120f, -3f, 3f);
                        if (Mathf.Abs(notches) < 0.05f) notches = Mathf.Sign(scroll); // touchpads / other scales
                        Distance *= Mathf.Pow(0.87f, notches);
                    }
                }
                if (mouse.rightButton.isPressed)
                {
                    Yaw += delta.x * 0.25f;
                    Pitch = Mathf.Clamp(Pitch - delta.y * 0.2f, MinPitch, 85f);
                }
                if (mouse.middleButton.isPressed)
                {
                    float k = Distance * 0.0022f;
                    if (delta != Vector2.zero) PlayerPanned = true;
                    Focus -= (right * delta.x + fwd * delta.y) * k;
                }
            }

            Distance = Mathf.Clamp(Distance, MinDistance, MaxDistance);
            Focus.x = Mathf.Clamp(Focus.x, BoundsMin.x, BoundsMax.x);
            Focus.z = Mathf.Clamp(Focus.z, BoundsMin.y, BoundsMax.y);
            Apply();
        }

        void Apply()
        {
            if (HeightAt != null) Focus.y = Mathf.Lerp(Focus.y, HeightAt(Focus.x, Focus.z), 0.2f);
            float k = _snap || !Glide ? 1f : 1f - Mathf.Exp(-Mathf.Min(Time.unscaledDeltaTime, 0.1f) * 13f);
            _snap = false;
            _sFocus = Vector3.Lerp(_sFocus, Focus, k);
            _sYaw = Mathf.Lerp(_sYaw, Yaw, k); _sPitch = Mathf.Lerp(_sPitch, Pitch, k); _sDist = Mathf.Lerp(_sDist, Distance, k);
            var rotation = Quaternion.Euler(_sPitch, _sYaw, 0);
            transform.SetPositionAndRotation(_sFocus + rotation * new Vector3(0, 0, -_sDist), rotation);
            if (Cam != null)
            {
                Cam.nearClipPlane = Mathf.Max(0.3f, _sDist * 0.02f);
                Cam.farClipPlane = _sDist * 4f + 400f;
            }
        }

        /// <summary>Ray from the mouse pointer through the camera, or null if there is no mouse.</summary>
        public Ray? PointerRay()
        {
            var mouse = Mouse.current;
            if (mouse == null || Cam == null) return null;
            return Cam.ScreenPointToRay(mouse.position.ReadValue());
        }
    }
}
