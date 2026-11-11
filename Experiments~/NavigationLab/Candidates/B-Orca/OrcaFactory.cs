using System.Collections.Generic;
using NavigationLab.Core;

namespace NavigationLab.Candidates.BOrca
{
    public sealed class OrcaFactory : INavigationFactory
    {
        public string Id => "B0-Orca";
        public string Description => "Pinned official ORCA + dynamic A* rerouting + exact static visibility simplification; no negotiated yield.";
        public NavigationFeatures Features => OrcaNavigator.SupportedFeatures;
        public INavigator Create(WorldDefinition world, IReadOnlyList<UnitProfile> profiles) => new OrcaNavigator(world, profiles, false);
    }

    public sealed class OrcaYieldFactory : INavigationFactory
    {
        public string Id => "B1-OrcaYield";
        public string Description => "B0 plus explicit temporary negotiated yield and vacancy promotion; reported separately from B0.";
        public NavigationFeatures Features => OrcaNavigator.SupportedFeatures;
        public INavigator Create(WorldDefinition world, IReadOnlyList<UnitProfile> profiles) => new OrcaNavigator(world, profiles, true);
    }
}
