using StardewModdingAPI;

namespace NewLanguageProject
{
    public class ModEntry : Mod
    {
        public override void Entry(IModHelper helper)
        {
            Monitor.Log("Mod loaded successfully!", LogLevel.Info);

            // test hook
            helper.Events.GameLoop.DayStarted += OnDayStarted;
        }

        private void OnDayStarted(object? sender, StardewModdingAPI.Events.DayStartedEventArgs e)
        {
            Monitor.Log("A new day started!", LogLevel.Info);
        }
    }
}