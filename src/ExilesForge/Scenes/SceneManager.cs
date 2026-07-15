// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Renderer;
using TEF.Input;

namespace TEF.Scenes
{
    /// <summary>
    /// ClassicUO's GameController owns a single `Scene` field and swaps it
    /// directly (see SetScene in ClassicUO.Client/GameController.cs). TEF pulls
    /// that into its own type so a scene can request a transition
    /// (RequestChange) without holding a reference back to GameController, and
    /// so the swap happens at a safe point (start of the next Update) instead
    /// of mid-frame.
    /// </summary>
    public sealed class SceneManager
    {
        private Scene _pending;

        public Scene Current { get; private set; }

        public void ChangeScene(Scene scene)
        {
            _pending = scene;
        }

        public void Update(InputManager input)
        {
            if (_pending != null)
            {
                Current?.Dispose();
                Current = _pending;
                _pending = null;
                Current.Load();
            }

            if (Current is { IsLoaded: true, IsDestroyed: false })
            {
                Current.Update(input);
            }
        }

        public void FixedUpdate(float fixedDelta)
        {
            if (Current is { IsLoaded: true, IsDestroyed: false })
            {
                Current.FixedUpdate(fixedDelta);
            }
        }

        public void Draw(UltimaBatcher2D batcher)
        {
            if (Current is { IsLoaded: true, IsDestroyed: false })
            {
                Current.Draw(batcher);
            }
        }
    }
}
