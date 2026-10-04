using ToolBelt.Tests.Framework;
using ToolBelt.Windows;

namespace ToolBelt.Windows.Tests
{
    public sealed class ElevationTests
    {
        public void Queries_AreConsistent()
        {
            bool elevated = Elevation.IsElevated();
            bool admin = Elevation.IsAdministrator();
            IntegrityLevel level = Elevation.CurrentIntegrityLevel();

            Check.True(level != IntegrityLevel.Unknown, $"integrity level resolved: {level}");

            // An elevated process runs at High (or System) integrity and is in the Administrators role.
            if (elevated)
            {
                Check.True(level == IntegrityLevel.High || level == IntegrityLevel.System,
                    $"elevated -> High/System, got {level}");
                Check.True(admin, "elevated -> in Administrators role");
            }
            else
            {
                // Non-elevated typically runs at Medium (or lower); never High+ purely from elevation.
                Check.True(level <= IntegrityLevel.Medium, $"non-elevated -> <= Medium, got {level}");
            }
        }

        public void Queries_AreRepeatable()
        {
            Elevation.IsElevated();
            Elevation.IsElevated();
            Elevation.CurrentIntegrityLevel();
            Elevation.CurrentIntegrityLevel();
        }
    }
}
