// Test-only Manager API models. The package implementations and native Unity lifecycle are not replaced in the project.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using Game.Framework;

namespace ECS
{
    public sealed class EntityManager : MonoSingleton<EntityManager>
    {
        protected override void OnExit()
        {
            ManagerContracts.Cleanup.Add("entities");
        }

        private bool initialized;
        protected override void OnStart()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            ManagerContracts.Calls.Add("entities");
        }
    }

    public sealed class WorldManager : MonoSingleton<WorldManager>
    {
        protected override void OnExit()
        {
            ManagerContracts.Cleanup.Add("worlds");
        }

        private bool initialized;
        protected override void OnStart()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            ManagerContracts.Calls.Add("worlds");
        }

        public override void Initialize()
        {
        }
    }
}
