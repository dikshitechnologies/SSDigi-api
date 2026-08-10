namespace CHITSCHEME.Global
{
    /// <summary>
    /// Promotional push notification templates grouped by time-of-day slot.
    ///
    /// Slot mapping (IST):
    ///   Morning   → 06:00 – 11:59
    ///   Afternoon → 12:00 – 16:59  (reuses Rate + Savings buckets)
    ///   Evening   → 17:00 – 20:59
    ///   Night     → 21:00 – 05:59
    ///
    /// Within each slot a random message is chosen from multiple themed buckets
    /// so the same user never sees the same text twice in a row.
    /// </summary>
    public static class PromotionalNotificationTemplates
    {
        // ── Morning messages (06:00 – 11:59) ────────────────────────────────────
        public static readonly (string Title, string Body)[] Morning =
        {
            ("Morning ☀️",          "Good morning! Your gold journey starts today."),
            ("Wake Up! 🌞",         "தங்கம் உங்களுக்காகக் காத்திருக்கிறது. இன்றைய விலையைச் சரிபாருங்கள்."),
            ("☕ Coffee & Gold",    "Coffee first, then check today's gold rate!"),
            ("💰 New Day",          "A new day, a new opportunity to save."),
            ("📈 Gold Update",      "Did gold move overnight? Find out now."),
            ("💛 Your Gold Future", "Your future gold starts with today's decision."),
            ("🌅 Start Small",      "Start small. Shine big. Begin your savings today."),
            ("💎 One Tap Away",     "One tap closer to your gold dream."),
            ("🔔 Daily Update",     "Your daily gold update is here."),
            ("🎯 Don't Miss It",    "Don't miss today's opportunity."),
            ("காலை வணக்கம்!","உங்கள் தங்கப் பயணம் இன்று தொடங்குகிறது."),

        };

        // ── Rate update messages (any time, used in afternoon slot) ─────────────
        public static readonly (string Title, string Body)[] RateUpdate =
        {
            ("📈 Rate Alert",       "Gold rates just changed! Check now."),
            ("🟡 Gold Moving",      "Gold is on the move. Tap to see the latest price."),
            ("🚨 Price Alert",      "Price alert! Check today's gold rate."),
            ("📈 Climbing Again",   "Gold is climbing again! See today's rate."),
            ("💰 Act Now",          "Your next gram may cost more. Check the rate."),
            ("🔥 Live Price",       "Today's price is live. Don't miss it."),
            ("👀 Today's Rate",     "Have you checked today's gold rate?"),
            ("📱 Fresh Prices",     "Fresh gold prices are waiting for you."),
            ("💎 Every Rupee",      "Every rupee matters today. Check now."),
            ("✨ Market Update",    "Tap to see today's gold market update.")
        };

        // ── Curiosity messages ───────────────────────────────────────────────────
        public static readonly (string Title, string Body)[] Curiosity =
        {
            ("🤫 Something New...", "Something interesting happened today. Open the app!"),
            ("👀 Guess What?",      "Guess what's new inside? Tap to find out."),
            ("🎁 For You",          "We've got something special for you."),
            ("💛 Your Surprise",    "Your next surprise is waiting inside."),
            ("🚪 Open & Smile",     "Open the app and smile — something's waiting."),
            ("✨ One Opportunity",  "One notification. One opportunity. Don't ignore it."),
            ("📱 Don't Ignore",     "Don't ignore this one. It's worth a look."),
            ("😏 Curious?",         "Curious? Tap to know more."),
            ("💎 Worth Checking",   "Today's update is definitely worth checking."),
            ("🔥 Shiny Surprise",   "Something shiny is waiting for you inside.")
        };

        // ── Savings messages ─────────────────────────────────────────────────────
        public static readonly (string Title, string Body)[] Savings =
        {
            ("💸 Save Today",       "Save today. Shine tomorrow."),
            ("🌟 ₹100 Can Do It",   "₹100 can start something beautiful."),
            ("💛 Every Drop Counts","Every little saving counts. Start now."),
            ("🪙 Build Your Gold",  "Build your gold one step at a time."),
            ("💰 Wallet Thanks",    "Your wallet will thank you for saving today."),
            ("📈 Save Consistently","Smart people save consistently. Be one of them."),
            ("✨ Tiny Savings",      "Tiny savings, huge dreams. Start today."),
            ("🏆 Gold Rewards",     "Gold rewards patience. Save a little every day."),
            ("💎 Save & Smile",     "Save today, smile tomorrow."),
            ("🌱 Grow Slowly",      "Grow your gold slowly and steadily.")
        };

        // ── FOMO messages ────────────────────────────────────────────────────────
        public static readonly (string Title, string Body)[] Fomo =
        {
            ("⏰ Time's Ticking",   "Time doesn't wait. Neither does gold."),
            ("📈 Prices Moving",    "Prices never ask permission. Check now."),
            ("😮 Don't Miss It",    "Don't let today's rate slip away."),
            ("💛 Limited Time",     "Today's opportunity won't last forever."),
            ("🔥 Gold Won't Wait",  "Gold won't wait. Tap before it changes."),
            ("📊 Before It Changes","Check the rate before it changes again."),
            ("📞 Gold Is Calling",  "Gold is calling. Will you answer?"),
            ("⚡ Don't Be Late",    "Don't be late. Today's rate is live."),
            ("🏃 Catch Today's Rate","Catch today's gold rate before it moves."),
            ("🚀 Act Now",          "Don't wait for tomorrow — act now.")
        };

        // ── Fun messages ─────────────────────────────────────────────────────────
        public static readonly (string Title, string Body)[] Fun =
        {
            ("🤔 Sleeping Savings?","Are your savings sleeping? Wake them up!"),
            ("😎 Smart Looks",      "Gold looks good on smart people."),
            ("💛 Shine Brighter",   "Shine a little brighter today."),
            ("📱 One Tap Away",     "Your future is one tap away."),
            ("🪙 Gold & Consistency","Gold loves consistency. Keep saving."),
            ("💎 Small Beginnings", "Dreams need small beginnings. Start today."),
            ("🎉 Good News",        "Good news is waiting inside the app."),
            ("🌟 Growing Savings",  "Smile... your savings are growing."),
            ("📈 Make It Count",    "Let's make today count."),
            ("💰 Big Happiness",    "One small deposit, big happiness.")
        };

        // ── Evening messages (17:00 – 20:59) ────────────────────────────────────
        public static readonly (string Title, string Body)[] Evening =
        {
            ("🌆 Smart Evening",    "End the day with smart savings."),
            ("🌙 Before You Sleep", "Before you sleep — check today's gold update."),
            ("⭐ Tonight's Rate",   "Have you checked today's rate yet?"),
            ("💛 Dream Never Sleeps","Your gold dream never sleeps. Neither does savings."),
            ("✨ One Last Reminder", "One last reminder for today — don't miss it."),
            ("🪙 Last Chance",      "Don't miss today's saving chance."),
            ("📊 Gold Never Sleeps","Gold doesn't sleep. Check the latest rate."),
            ("🌟 End With Progress","End today with progress. Save something."),
            ("📱 What's New",       "Check what's new before the day ends."),
            ("🎯 Closer Than Yesterday","Your goal is closer than yesterday.")
        };

        // ── Offers messages ──────────────────────────────────────────────────────
        public static readonly (string Title, string Body)[] Offers =
        {
            ("🎁 Surprise Inside",  "A surprise is waiting inside the app."),
            ("💎 Special Benefits", "Special benefits are available today."),
            ("🎉 Limited-Time",     "Limited-time reward — grab it now."),
            ("✨ Discover More",    "Open now to discover what's new."),
            ("🏆 Today's Offer",    "Today's offer won't last. Tap to see it."),
            ("🔥 Exclusive",        "Exclusive deal — just for you."),
            ("💰 More Benefits",    "More benefits available today than yesterday."),
            ("🎊 Daily Surprise",   "Don't miss today's surprise offer."),
            ("💛 Unlock Savings",   "Tap to unlock today's savings."),
            ("🎁 Ready For You",    "Something special is ready. Open the app.")
        };

        // ── Friendly / re-engagement messages ───────────────────────────────────
        public static readonly (string Title, string Body)[] Friendly =
        {
            ("👋 Missed You",       "We missed you. Let's continue your gold journey."),
            ("💛 Welcome Back",     "Welcome back! Good to see you again."),
            ("😊 Still Here",       "We're still here, growing your gold with you."),
            ("🌟 Good To See You",  "Good to see you again! Check what's new."),
            ("💎 Ready Today?",     "Ready for today's update?"),
            ("📈 Your Progress",    "Your progress matters to us. Keep going."),
            ("🪙 Keep Streak Alive","Keep your savings streak alive today."),
            ("💰 Every Day Counts", "Every day is a saving day. Don't skip today."),
            ("🎯 You're Doing Great","You're doing great. Keep it up!"),
            ("🚀 Grow Together",    "Let's grow together — one gram at a time.")
        };

        // ── Night / late-night messages (21:00 – 05:59) ─────────────────────────
        public static readonly (string Title, string Body)[] Night =
        {
            ("🌙 Late Night Gold",  "Gold never sleeps. Check tomorrow's opening rate."),
            ("✨ Sweet Dreams",      "Sweet dreams! Your savings are growing overnight."),
            ("💛 Night Thought",    "A little saving today = a brighter tomorrow."),
            ("🪙 Night Check",      "Quick gold check before bed — tap to see the rate."),
            ("💎 Tomorrow's Gold",  "Plan your gold savings for tomorrow. Start now."),
            ("🌟 End Well",         "End tonight well — check today's gold summary."),
            ("🔥 Night Update",     "Tonight's gold update is ready for you."),
            ("📊 Sleep & Save",     "You rest, your savings grow. Check the rate."),
            ("💰 Night Reminder",   "Don't sleep without saving for tomorrow."),
            ("🎯 Plan Ahead",       "Plan your gold savings tonight for a golden tomorrow.")
        };

        // ── Random bucket (wildcard, any time) ───────────────────────────────────
        public static readonly (string Title, string Body)[] RandomMessages =
        {
            ("✨ Gold Loves You",   "Gold loves patient people. Keep saving."),
            ("💛 Every Gram",       "Every gram tells a story. Start yours today."),
            ("📱 Best Decision",    "Today's best decision starts here."),
            ("🌟 Shine Smart",      "Shine with smart savings."),
            ("🏆 Small Steps",      "Small steps. Golden future. Start today."),
            ("💰 Today's Treasure", "Today's savings are tomorrow's treasure."),
            ("📈 Every Opportunity","Every notification is an opportunity. Tap now."),
            ("🪙 Future Self",      "Your future self is smiling. Save today."),
            ("💎 Golden Day",       "Make today a golden day."),
            ("🚀 Build Together",   "Let's build your golden future together.")
        };

        // ── Time-slot resolver ────────────────────────────────────────────────────
        /// <summary>
        /// Returns the primary message bucket for the given IST hour (0–23).
        /// A secondary random injection (1-in-4 chance) pulls from Curiosity or Fun
        /// to keep notifications fresh.
        /// </summary>
        public static (string Title, string Body)[] GetBucketForHour(int hour)
        {
            return hour switch
            {
                >= 6 and <= 11  => Morning,
                >= 12 and <= 16 => RateUpdate,   // afternoon: rate + savings feel
                >= 17 and <= 20 => Evening,
                _               => Night          // 21–05
            };
        }

        /// <summary>
        /// Returns a random (Title, Body) pair appropriate for the given hour.
        /// Every 4th call injects a message from the Curiosity, Savings, FOMO, Fun,
        /// Offers, or Friendly bucket to keep content varied.
        /// </summary>
        public static (string Title, string Body) GetRandom(int hour)
        {
            var rng = new Random();

            // 25% of the time → pick a themed "variety" bucket
            int variety = rng.Next(4);
            if (variety == 0)
            {
                var varietyBuckets = new[]
                {
                    Curiosity, Savings, Fomo, Fun, Offers, Friendly, RandomMessages
                };
                var bucket = varietyBuckets[rng.Next(varietyBuckets.Length)];
                return bucket[rng.Next(bucket.Length)];
            }

            // 75% → time-appropriate bucket
            var main = GetBucketForHour(hour);
            return main[rng.Next(main.Length)];
        }
    }
}
