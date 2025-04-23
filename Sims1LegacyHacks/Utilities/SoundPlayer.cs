using System.Media;
using System.Reflection;

namespace Sims1LegacyHacks.Utilities;

public static class SoundPlayer
{
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static void PlaySound()
    {
        try
        {
            const string resourceName = "Sims1LegacyHacks.Resources.button-202966.wav";
            var assembly = Assembly.GetExecutingAssembly();

            using var resourceStream = assembly.GetManifestResourceStream(resourceName);
            if (resourceStream != null)
            {
                var player = new System.Media.SoundPlayer(resourceStream);
                player.Play();
            }
            else
            {
                Console.WriteLine($"Error: Embedded resource not found: {resourceName}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"An error occurred while trying to play the embedded sound: {ex.Message}"
            );
        }
    }
}
