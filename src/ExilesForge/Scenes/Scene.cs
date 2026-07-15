// SPDX-License-Identifier: BSD-2-Clause

using System;
using ClassicUO.Renderer;
using TEF.Core;
using TEF.Input;

namespace TEF.Scenes
{
    /// <summary>
    /// Modeled on ClassicUO.Game.Scenes.Scene: same Load/Unload/Update/Draw
    /// lifecycle and a per-scene Camera. Input hooks are simplified to plain
    /// "did this happen this frame" queries against InputManager rather than
    /// raw SDL event callbacks, since TEF has no gump/control layer competing
    /// for the same events.
    /// </summary>
    public abstract class Scene : IDisposable
    {
        protected Scene(GameController game)
        {
            Game = game;
        }

        protected GameController Game { get; }

        public bool IsLoaded { get; private set; }
        public bool IsDestroyed { get; private set; }

        public Camera Camera { get; } = new Camera(minZoomValue: 0.5f, maxZoomValue: 2.5f, zoomStep: 0.1f);

        public virtual void Load()
        {
            IsLoaded = true;
        }

        public virtual void Unload()
        {
            IsLoaded = false;
        }

        public virtual void Update(InputManager input)
        {
        }

        /// <summary>Fixed-step simulation tick (see SimulationClock) - survival timers, resource respawn, etc. should hang off this, not Update's variable Delta.</summary>
        public virtual void FixedUpdate(float fixedDelta)
        {
        }

        public virtual void Draw(UltimaBatcher2D batcher)
        {
        }

        public void Dispose()
        {
            if (IsDestroyed)
            {
                return;
            }

            Unload();
            IsDestroyed = true;
        }
    }
}
