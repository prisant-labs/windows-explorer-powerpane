using System;
using System.Globalization;
using System.Windows.Threading;

namespace QuickPane.Services
{
    /// <summary>Tells the user when a newer release exists without being asked (#176). At most once a
    /// day, some minutes after startup, it runs the same <see cref="UpdateService.CheckAsync"/> the
    /// About page runs. A newer version raises one notice per version, and stays visible as a tray
    /// menu item and a dot on About until the running build catches up. It never downloads or
    /// installs anything: every path out of a notice opens the release page in the browser.
    ///
    /// The last-check time lives in settings, so a restart does not cause an extra request. It is
    /// recorded before the request goes out, so a machine that is offline waits for the next day
    /// rather than retrying every hour: the promise is "at most once a day", which is what the
    /// README's privacy section states.</summary>
    internal static class UpdateNotifier
    {
        internal static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

        // Far enough after startup that sign-in, when the network and the disk are at their busiest,
        // is over. The hourly tick after it only reads a timestamp, so it costs nothing on most ticks,
        // and it is what catches the check falling due on a machine that stays signed in for days.
        private static readonly TimeSpan FirstTickDelay = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan TickInterval = TimeSpan.FromHours(1);

        private static DispatcherTimer _timer;
        private static Action<Version> _showNotice;

        /// <summary>The newer version known to exist, or null. Read on the UI thread.</summary>
        public static Version Available { get; private set; }

        /// <summary>Raised on the UI thread when <see cref="Available"/> changes.</summary>
        public static event Action AvailabilityChanged;

        /// <summary>Starts the timer. <paramref name="showNotice"/> shows the one-time notice for a
        /// newly found version. Restores what an earlier session learned, so the tray item and the
        /// dot do not vanish for up to a day because of a restart.</summary>
        public static void Start(Action<Version> showNotice)
        {
            _showNotice = showNotice;
            var settings = App.Settings;
            Version noticed;
            if (settings != null && TryReadVersion(settings.Current.UpdateNoticeVersion, out noticed) && noticed > UpdateService.Current)
                SetAvailable(noticed);

            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = FirstTickDelay };
            _timer.Tick += (s, e) =>
            {
                _timer.Interval = TickInterval;
                CheckIfDue();
            };
            _timer.Start();
        }

        public static void Stop()
        {
            if (_timer != null) _timer.Stop();
            _timer = null;
        }

        private static void CheckIfDue()
        {
            var settings = App.Settings;
            if (settings == null) return;
            var now = DateTime.UtcNow;
            if (!IsCheckDue(settings.Current.UpdateCheckOff, settings.Current.LastUpdateCheckUtc, now)) return;

            settings.Current.LastUpdateCheckUtc = FormatTime(now);
            settings.Save();
            UpdateService.CheckAsync(r =>
            {
                Record(r);
                if (r == null || !string.IsNullOrEmpty(r.Error) || !r.Available) return;
                var current = App.Settings;
                if (current == null || !ShouldNotify(r.Latest, UpdateService.Current, current.Current.UpdateNoticeVersion)) return;
                MarkNoticed(r.Latest);
                var show = _showNotice;
                if (show != null) show(r.Latest);
            });
        }

        /// <summary>What the About page's own check found. The user is looking at the answer already,
        /// so a later automatic check must not announce the same version again.</summary>
        public static void ReportAboutResult(UpdateCheckResult r)
        {
            Record(r);
            if (r != null && string.IsNullOrEmpty(r.Error) && r.Available) MarkNoticed(r.Latest);
        }

        private static void MarkNoticed(Version latest)
        {
            var settings = App.Settings;
            if (settings == null) return;
            settings.Current.UpdateNoticeVersion = UpdateService.Display(latest);
            settings.Save();
        }

        // A failed check says nothing about what exists, so it leaves the last answer in place.
        private static void Record(UpdateCheckResult r)
        {
            if (r == null || !string.IsNullOrEmpty(r.Error)) return;
            SetAvailable(r.Available ? r.Latest : null);
        }

        private static void SetAvailable(Version version)
        {
            if (Equals(Available, version)) return;
            Available = version;
            var h = AvailabilityChanged;
            if (h != null) h();
        }

        // ---- pure decisions, unit-tested ----------------------------------------------------

        /// <summary>Whether an automatic check should run now. An unreadable time, or one in the
        /// future because the clock moved back, counts as due: otherwise a bad value would stop the
        /// checks for good.</summary>
        internal static bool IsCheckDue(bool checkOff, string lastCheckUtc, DateTime nowUtc)
        {
            if (checkOff) return false;
            DateTime last;
            if (!TryReadTime(lastCheckUtc, out last)) return true;
            if (last > nowUtc) return true;
            return nowUtc - last >= CheckInterval;
        }

        /// <summary>Whether to raise the one-time notice: the version is newer than the running build,
        /// and newer than the last version the user was told about.</summary>
        internal static bool ShouldNotify(Version latest, Version current, string lastNoticed)
        {
            if (latest == null || current == null || latest <= current) return false;
            Version noticed;
            return !TryReadVersion(lastNoticed, out noticed) || latest > noticed;
        }

        /// <summary>A round-trip UTC timestamp. Stored as text because DataContractJsonSerializer
        /// writes a DateTime in its own "/Date(...)/" form, which nobody can read in settings.json.</summary>
        internal static string FormatTime(DateTime utc)
        {
            return utc.ToString("o", CultureInfo.InvariantCulture);
        }

        private static bool TryReadTime(string text, out DateTime utc)
        {
            utc = default(DateTime);
            if (string.IsNullOrWhiteSpace(text)) return false;
            DateTime parsed;
            if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed)) return false;
            utc = parsed.ToUniversalTime();
            return true;
        }

        private static bool TryReadVersion(string text, out Version version)
        {
            version = null;
            return !string.IsNullOrWhiteSpace(text) && Version.TryParse(text, out version);
        }

        /// <summary>The balloon's and the tray item's wording, in one place.</summary>
        internal static string NoticeTitle(Version latest)
        {
            return "QuickPane " + UpdateService.Display(latest) + " is available";
        }
    }
}
