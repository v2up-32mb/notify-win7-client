using System;
using System.Media;

// Tiny sound: system asterisk, no wav files, no extra memory.
static class Sounder
{
    public static void Notify()
    {
        try
        {
            if (!AppConfig.SoundEnabled) return;
            SystemSounds.Asterisk.Play();
        }
        catch { }
    }
}
