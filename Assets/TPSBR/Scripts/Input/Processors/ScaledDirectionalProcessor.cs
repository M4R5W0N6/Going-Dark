namespace TPSBR
{
    using UnityEngine;
    using UnityEngine.InputSystem;
    using UnityEngine.InputSystem.Controls;

#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoad]
#endif
    public sealed class ScaledDirectionalProcessor : InputProcessor<Vector2>
    {
        public float walkScale = 0.75f;

        static ScaledDirectionalProcessor()
        {
            Register();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            InputSystem.RegisterProcessor<ScaledDirectionalProcessor>("ScaledDirectional");
        }

        public override Vector2 Process(Vector2 value, InputControl control)
        {
            if (control != null && control.device is Keyboard)
            {
                Keyboard keyboard = Keyboard.current;
                if (keyboard != null && keyboard.leftShiftKey.isPressed)
                {
                    return value;
                }

                return value * Mathf.Clamp01(walkScale);
            }

            return value;
        }
    }
}
