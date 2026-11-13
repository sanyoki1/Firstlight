using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Firstlight
{
    /// <summary>
    /// Entry point of the Game scene. "-stresstest" on the command line jumps to the stress test scene;
    /// otherwise it starts the game.
    /// </summary>
    public sealed class Bootstrap : MonoBehaviour
    {
        void Start()
        {
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-stresstest") >= 0 || Array.IndexOf(args, "-benchmark") >= 0)
            {
                SceneManager.LoadScene("StressTest");
                return;
            }
            var type = Type.GetType("Firstlight.Game.GameController");
            if (type != null) gameObject.AddComponent(type);
            else SceneManager.LoadScene("StressTest");
        }
    }
}
