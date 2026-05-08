using System.Drawing;

namespace PetMapMarkers;

public class ModConfig
{
    // main update interval in seconds
    public float UpdateIntervalSeconds { get; set; } = 2;

    // visual defaults
    public string DefaultColor { get; set; } = "#ffffff";
    public string DefaultIcon { get; set; } = "pawprint";
    public string DownedColor { get; set; } = "#ff0000";

    // whether to track only domesticated pets or also those in the process of taming
    public bool TrackTamingPets { get; set; } = true;

    // full world scan interval in minutes; -1 to disable
    public int FullScanMinutes { get; set; } = 5;

    public static int ColorStringToArgb(string nameOrHex)
    {
        var c = ColorTranslator.FromHtml(nameOrHex);
        c = Color.FromArgb(255, c.R, c.G, c.B);
        return c.ToArgb();
    }
}
