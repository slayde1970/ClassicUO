// SPDX-License-Identifier: BSD-2-Clause

using System;
using ClassicUO.Renderer;
using UOA.Core;
using UOA.Input;

namespace UOA.Scenes
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

        /// <summary>
        /// Called once when the host is cleanly exiting (window close /
        /// Game.Exit), before shutdown. A scene that owns unsaved session
        /// state overrides this to flush it (e.g. WorldScene saves the game).
        /// Lets the engine host trigger a save without knowing any concrete
        /// game scene type (Tier 4.5).
        /// </summary>
        public virtual void OnHostExiting()
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
