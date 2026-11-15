// Action group voicelines, the pure part: the supported action groups, one voiceline, the configuration a craft
// stores, the built-in phrase library (Phrases/action_groups.tsv), the random pick and the edge detector.
// Pure code: no Unity or KSP references, so it compiles under DC_OFFLINE_TEST.
using System;
using System.Collections.Generic;
using System.Text;

namespace Lonapse
{
    /// <summary>One KSP action group that can carry voicelines.</summary>
    public sealed class ActionGroupDef
    {
        public string Key, Label, CooldownKey;
        /// <summary>The KSPActionGroup value (a single bit).</summary>
        public int Bit;
        /// <summary>The state of the group says something (gear down or up). Abort and the custom groups flip their
        /// state on every press, so there the state means nothing and every press counts the same.</summary>
        public bool OnOff;
        /// <summary>Abort: its line goes before the chatter.</summary>
        public bool Urgent;
        public bool Custom;
    }

    public static class ActionVoiceGroups
    {
        public static readonly ActionGroupDef[] All = Build();

        static ActionGroupDef[] Build()
        {
            var r = new List<ActionGroupDef>();
            r.Add(Def("abort", "Abort", 0x40, false, true, false));
            r.Add(Def("gear", "Gear", 0x02, true, false, false));
            r.Add(Def("lights", "Lights", 0x04, true, false, false));
            r.Add(Def("brakes", "Brakes", 0x20, true, false, false));
            r.Add(Def("rcs", "RCS", 0x08, true, false, false));
            r.Add(Def("sas", "SAS", 0x10, true, false, false));
            for (int i = 1; i <= 10; i++)
                r.Add(Def("custom" + i.ToString("00"), "Custom " + i, 0x80 << (i - 1), false, false, true));
            return r.ToArray();
        }

        static ActionGroupDef Def(string key, string label, int bit, bool onOff, bool urgent, bool custom)
        {
            return new ActionGroupDef { Key = key, Label = label, Bit = bit, OnOff = onOff, Urgent = urgent, Custom = custom,
                                        CooldownKey = "action:" + key };
        }

        public static int IndexOf(string key)
        {
            for (int i = 0; i < All.Length; i++) if (All[i].Key == key) return i;
            return -1;
        }
    }

    /// <summary>What the random pick needs of a voiceline or a probe beep.</summary>
    public interface IActionLine
    {
        string GroupKey { get; }
        bool Usable { get; }
        bool Fits(bool onOff, bool on);
    }

    /// <summary>One voiceline of an action group.</summary>
    public sealed class ActionVoiceLine : IActionLine
    {
        public const string StateAny = "any", StateOn = "on", StateOff = "off";
        public const string SpeakerMc = "mc", SpeakerAuto = "auto", SpeakerKerbal = "kerbal";
        /// <summary>The longest text a player can enter. A longer line takes Piper seconds and holds the radio.</summary>
        public const int MaxText = 140;

        public string Group = "";
        /// <summary>When the line fits in a group with a state: on, off or any.</summary>
        public string State = StateAny;
        public string Speaker = SpeakerAuto;
        /// <summary>The raw name of the kerbal for SpeakerKerbal.</summary>
        public string Kerbal = "";
        /// <summary>What the player reads and typed: the subtitle.</summary>
        public string Text = "";
        /// <summary>What the voice says, for a built-in line. Empty: the voice says Text put into Spanish (SpokenWith).
        /// Both become loñapsE.</summary>
        public string Spanish = "";
        public bool Intercom;
        public bool Enabled = true;
        /// <summary>The line came from the built-in library and its text was not changed.</summary>
        public bool BuiltIn;
        /// <summary>Mission Control answers the line. Only read for a line the player wrote: a built-in line is
        /// always answered.</summary>
        public bool ReplyOn;
        /// <summary>The answer of Mission Control, as the player typed it. Empty with ReplyOn: one of the standard
        /// answers of the library. In the library: the answer written for this row.</summary>
        public string Reply = "";
        /// <summary>The Spanish of Reply, for a row of the library only.</summary>
        public string ReplySpanish = "";

        public bool IsMc { get { return Speaker == SpeakerMc; } }
        public string GroupKey { get { return Group; } }

        /// <summary>The line can be picked: switched on and with something to say.</summary>
        public bool Usable { get { return Enabled && !string.IsNullOrEmpty(Text) && Text.Trim().Length > 0; } }

        /// <summary>Mission Control answers this line: a kerbal says it on the radio, and it is a built-in line or the
        /// player asked for an answer. Mission Control does not hear the intercom and does not answer itself.</summary>
        public bool WantsReply { get { return !IsMc && !Intercom && (BuiltIn || ReplyOn); } }

        /// <summary>The text for the voice without a dictionary: the Spanish of a built-in line, else Text as typed.</summary>
        public string Spoken { get { return string.IsNullOrEmpty(Spanish) ? Text : Spanish; } }

        /// <summary>The text for the voice, before the loñapsE step: the Spanish of a built-in line, else Text put
        /// into Spanish by the dictionary. The result can hold name marks, so it goes to RuntimeSpeech as a template.</summary>
        public string SpokenWith(EnglishSpanish dictionary)
        {
            if (!string.IsNullOrEmpty(Spanish) || dictionary == null) return Spoken;
            return dictionary.Translate(Clean(Text));
        }

        /// <summary>The line fits an activation. onOff false (Abort, a custom group): every line fits.</summary>
        public bool Fits(bool onOff, bool on)
        {
            if (!onOff || State == StateAny) return true;
            return State == (on ? StateOn : StateOff);
        }

        public ActionVoiceLine Clone()
        {
            return new ActionVoiceLine { Group = Group, State = State, Speaker = Speaker, Kerbal = Kerbal, Text = Text, Spanish = Spanish,
                                         Intercom = Intercom, Enabled = Enabled, BuiltIn = BuiltIn,
                                         ReplyOn = ReplyOn, Reply = Reply, ReplySpanish = ReplySpanish };
        }

        /// <summary>Makes the fields valid: a known state and speaker, Mission Control on the radio, a clean text.</summary>
        public void Normalise()
        {
            if (State != StateOn && State != StateOff) State = StateAny;
            if (Speaker != SpeakerMc && Speaker != SpeakerKerbal) Speaker = SpeakerAuto;
            if (Speaker != SpeakerKerbal) Kerbal = "";
            else if (string.IsNullOrEmpty(Kerbal)) Speaker = SpeakerAuto;
            if (Speaker == SpeakerMc) Intercom = false;   // Mission Control is not in the cabin
            Text = Clean(Text);
            Spanish = Clean(Spanish);
            Reply = Clean(Reply);
            ReplySpanish = Clean(ReplySpanish);
        }

        /// <summary>The characters a line may not hold: control characters, the marks RuntimeSpeech reads as a name,
        /// and the slot and version characters of the phrase tables.</summary>
        static bool Forbidden(char c)
        {
            return char.IsControl(c) || c == RuntimeSpeech.NameOpen || c == RuntimeSpeech.NameClose || c == RuntimeSpeech.RawOpen
                   || c == RuntimeSpeech.RawClose || c == '{' || c == '}' || c == '|';
        }

        /// <summary>While the player types: the forbidden characters go and the length is capped. Spaces stay.</summary>
        public static string Filter(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(Math.Min(s.Length, MaxText));
            foreach (char c in s)
            {
                if (sb.Length >= MaxText) break;
                if (c == '\t' || c == '\n') { sb.Append(' '); continue; }
                if (!Forbidden(c)) sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>A stored text: filtered, runs of spaces as one, no space at the ends.</summary>
        public static string Clean(string s)
        {
            string f = Filter(s);
            var sb = new StringBuilder(f.Length);
            bool space = false;
            foreach (char c in f)
            {
                if (char.IsWhiteSpace(c)) { space = sb.Length > 0; continue; }
                if (space) sb.Append(' ');
                space = false;
                sb.Append(c);
            }
            return sb.ToString();
        }
    }

    /// <summary>One beep a probe gives when an action group fires. A probe has no kerbal to say a voiceline, so on
    /// a probe one of the beeps of the group plays instead, picked like a voiceline, and Mission Control can answer it.</summary>
    public sealed class ProbeBeepLine : IActionLine
    {
        /// <summary>The most words after "beeps" in the subtitle, like the probe rows of the phrase tables.</summary>
        public const int MaxWords = 3, MaxText = 40;

        public string Group = "";
        /// <summary>When the beep fits in a group with a state: on, off or any.</summary>
        public string State = ActionVoiceLine.StateAny;
        /// <summary>A ProbeCores mood. Anything else is no beep.</summary>
        public string Mood = "";
        /// <summary>The words after "beeps" in the subtitle. Empty: the word of the mood, like "nervously".</summary>
        public string Words = "";
        public bool Enabled = true;
        /// <summary>Mission Control answers the beep.</summary>
        public bool ReplyOn = true;
        /// <summary>The answer of Mission Control, in English. Empty with ReplyOn: one of the standard answers.</summary>
        public string Reply = "";
        /// <summary>The Spanish of Reply, for a built-in beep whose answer was not changed. Empty: Reply goes
        /// through the dictionary.</summary>
        public string ReplySpanish = "";

        public string GroupKey { get { return Group; } }

        public bool HasMood { get { return Array.IndexOf(ProbeCores.Moods, Mood) >= 0; } }

        /// <summary>The beep can be picked: switched on and with a mood.</summary>
        public bool Usable { get { return Enabled && HasMood; } }

        public string Subtitle { get { return "[beeps " + (Words.Length > 0 ? Words : ProbeRules.MoodAdverb(Mood)) + "]"; } }

        public bool Fits(bool onOff, bool on)
        {
            if (!onOff || State == ActionVoiceLine.StateAny) return true;
            return State == (on ? ActionVoiceLine.StateOn : ActionVoiceLine.StateOff);
        }

        public ProbeBeepLine Clone()
        {
            return new ProbeBeepLine { Group = Group, State = State, Mood = Mood, Words = Words, Enabled = Enabled,
                                       ReplyOn = ReplyOn, Reply = Reply, ReplySpanish = ReplySpanish };
        }

        public void Normalise()
        {
            if (State != ActionVoiceLine.StateOn && State != ActionVoiceLine.StateOff) State = ActionVoiceLine.StateAny;
            if (!HasMood) Mood = "";
            Words = CleanWords(Words);
            Reply = ActionVoiceLine.Clean(Reply);
            ReplySpanish = Reply.Length == 0 ? "" : ActionVoiceLine.Clean(ReplySpanish);
        }

        /// <summary>Lowercase letters and single spaces, at most MaxWords words. The subtitle then reads like the
        /// beeps of the phrase tables.</summary>
        public static string CleanWords(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s.ToLowerInvariant())
            {
                if (c >= 'a' && c <= 'z') sb.Append(c);
                else if (sb.Length > 0 && sb[sb.Length - 1] != ' ') sb.Append(' ');
            }
            string[] words = sb.ToString().Trim().Split(' ');
            if (words.Length > MaxWords) Array.Resize(ref words, MaxWords);
            string r = string.Join(" ", words);
            return r.Length > MaxText ? r.Substring(0, MaxText).Trim() : r;
        }

        /// <summary>What a player may type: letters and spaces, nothing longer than MaxText.</summary>
        public static string Filter(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s.ToLowerInvariant()) if ((c >= 'a' && c <= 'z') || c == ' ') sb.Append(c);
            return sb.Length > MaxText ? sb.ToString(0, MaxText) : sb.ToString();
        }
    }

    /// <summary>
    /// The voicelines of one craft, as stored in ModuleLonapseVoicelines. A craft without stored data follows the
    /// built-in library. The first change in the editor stores every line, so later library changes and Lonapse
    /// updates never touch a craft the player set up.
    /// </summary>
    public sealed class ActionVoiceConfig
    {
        public const int Version = 1;
        /// <summary>The most lines a craft stores. The data is copied to every command part.</summary>
        public const int MaxLines = 96;
        /// <summary>The most probe beeps a craft stores.</summary>
        public const int MaxBeeps = 64;
        const string BeepTag = "probe";

        public readonly List<ActionVoiceLine> Lines = new List<ActionVoiceLine>();
        /// <summary>The probe beeps. One of those that fit is picked per activation, like a voiceline.</summary>
        public readonly List<ProbeBeepLine> Beeps = new List<ProbeBeepLine>();
        /// <summary>The data had probe beeps. Data from before the beeps gets the built-in ones.</summary>
        public bool HasBeeps;

        public int BeepCount(string group)
        {
            int n = 0;
            for (int i = 0; i < Beeps.Count; i++) if (Beeps[i].Group == group) n++;
            return n;
        }

        public void RemoveBeeps(string group)
        {
            Beeps.RemoveAll(b => b.Group == group);
        }

        public int Count(string group)
        {
            int n = 0;
            for (int i = 0; i < Lines.Count; i++) if (Lines[i].Group == group) n++;
            return n;
        }

        public void RemoveGroup(string group)
        {
            Lines.RemoveAll(l => l.Group == group);
        }

        public ActionVoiceConfig Clone()
        {
            var c = new ActionVoiceConfig();
            foreach (ActionVoiceLine l in Lines) c.Lines.Add(l.Clone());
            foreach (ProbeBeepLine b in Beeps) c.Beeps.Add(b.Clone());
            c.HasBeeps = HasBeeps;
            return c;
        }

        /// <summary>One line of text that is safe as a ConfigNode value: "1;" and then every voiceline, fields split
        /// by | and lines by ;
        /// (group, state, speaker, kerbal, mode, enabled, built-in, text, spanish, reply on, reply). A config without lines still gives "1;", so "the player removed everything" is not
        /// read as "nothing stored". The probe beeps follow as rows
        /// "probe|group|state|mood|words|reply on|enabled|reply|reply spanish". A version from before the beeps
        /// reads such a row as a line of the unknown group "probe" and skips it.</summary>
        public string Encode()
        {
            var sb = new StringBuilder(64 + Lines.Count * 96);
            sb.Append(Version).Append(';');
            for (int i = 0; i < Lines.Count && i < MaxLines; i++)
            {
                ActionVoiceLine l = Lines[i];
                if (i > 0) sb.Append(';');
                Esc(sb, l.Group).Append('|');
                Esc(sb, l.State).Append('|');
                Esc(sb, l.Speaker).Append('|');
                Esc(sb, l.Kerbal).Append('|');
                sb.Append(l.Intercom ? 'i' : 'r').Append('|');
                sb.Append(l.Enabled ? '1' : '0').Append('|');
                sb.Append(l.BuiltIn ? '1' : '0').Append('|');
                Esc(sb, l.Text).Append('|');
                Esc(sb, l.Spanish).Append('|');
                sb.Append(l.ReplyOn ? '1' : '0').Append('|');
                Esc(sb, l.Reply);
            }
            bool first = Lines.Count == 0;
            for (int i = 0; i < Beeps.Count && i < MaxBeeps; i++)
            {
                ProbeBeepLine b = Beeps[i];
                if (!first) sb.Append(';');
                first = false;
                sb.Append(BeepTag).Append('|');
                Esc(sb, b.Group).Append('|');
                Esc(sb, b.State).Append('|');
                Esc(sb, b.Mood).Append('|');
                Esc(sb, b.Words).Append('|');
                sb.Append(b.ReplyOn ? '1' : '0').Append('|');
                sb.Append(b.Enabled ? '1' : '0').Append('|');
                Esc(sb, b.Reply).Append('|');
                Esc(sb, b.ReplySpanish);
            }
            return sb.ToString();
        }

        /// <summary>Reads stored data. Null for nothing stored or data that cannot be read (the craft then follows
        /// the library). Lines of an unknown group and fields a newer version added are skipped.</summary>
        public static ActionVoiceConfig Decode(string data)
        {
            if (string.IsNullOrEmpty(data)) return null;
            data = data.Trim();
            int sep = data.IndexOf(';');
            int version;
            if (sep <= 0 || !int.TryParse(data.Substring(0, sep), out version) || version < 1) return null;
            var c = new ActionVoiceConfig();
            string rest = data.Substring(sep + 1);
            if (rest.Length == 0) return c;
            foreach (string row in rest.Split(';'))
            {
                string[] f = row.Split('|');
                if (f.Length >= 6 && f[0] == BeepTag)
                {
                    c.HasBeeps = true;
                    var b = new ProbeBeepLine { Group = Unesc(f[1]), State = Unesc(f[2]), Mood = Unesc(f[3]), Words = Unesc(f[4]), ReplyOn = f[5] == "1" };
                    // a row with six fields is from before a group could hold more than one beep
                    if (f.Length >= 9) { b.Enabled = f[6] == "1"; b.Reply = Unesc(f[7]); b.ReplySpanish = Unesc(f[8]); }
                    if (ActionVoiceGroups.IndexOf(b.Group) < 0 || c.Beeps.Count >= MaxBeeps) continue;
                    b.Normalise();
                    // a beep without a mood was "no beep here" in the older data
                    if (b.HasMood) c.Beeps.Add(b);
                    continue;
                }
                if (f.Length < 9 || c.Lines.Count >= MaxLines) continue;
                var l = new ActionVoiceLine
                {
                    Group = Unesc(f[0]), State = Unesc(f[1]), Speaker = Unesc(f[2]), Kerbal = Unesc(f[3]),
                    Intercom = f[4] == "i", Enabled = f[5] == "1", BuiltIn = f[6] == "1", Text = Unesc(f[7]), Spanish = Unesc(f[8]),
                };
                // data from before the answers of Mission Control has nine fields: a line of the player gets no answer
                if (f.Length >= 11) { l.ReplyOn = f[9] == "1"; l.Reply = Unesc(f[10]); }
                if (ActionVoiceGroups.IndexOf(l.Group) < 0) continue;
                l.Normalise();
                c.Lines.Add(l);
            }
            return c;
        }

        // Only ASCII letters, digits, spaces and plain punctuation are written as they are. Everything else is
        // %XXXX (the UTF-16 unit in hex), so a save or craft file never sees a character it reads as syntax.
        static StringBuilder Esc(StringBuilder sb, string s)
        {
            if (s == null) return sb;
            foreach (char c in s)
            {
                bool plain = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')
                             || c == ' ' || c == '.' || c == ',' || c == '!' || c == '?' || c == '\'' || c == '-' || c == '_';
                if (plain) sb.Append(c);
                else sb.Append('%').Append(((int)c).ToString("X4"));
            }
            return sb;
        }

        static string Unesc(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('%') < 0) return s ?? "";
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                int code;
                if (s[i] == '%' && i + 4 < s.Length
                    && int.TryParse(s.Substring(i + 1, 4), System.Globalization.NumberStyles.HexNumber,
                                    System.Globalization.CultureInfo.InvariantCulture, out code))
                {
                    sb.Append((char)code);
                    i += 4;
                }
                else sb.Append(s[i]);
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// The built-in phrases, from Phrases/action_groups.tsv: group, state, speaker, mode, default, spanish, english.
    /// group is an action group key, "custom" (a preset for every custom group, never assigned by itself) or "reply"
    /// (a standard answer of Mission Control). default on: the line is switched on for a craft that has no
    /// voicelines of its own. The columns reply_spanish and reply_english are optional and hold the answer Mission
    /// Control gives to that row.
    /// </summary>
    public sealed class ActionVoiceLibrary
    {
        public const string CustomGroup = "custom", ReplyGroup = "reply";
        static readonly string[] Columns = { "group", "state", "speaker", "mode", "default", "spanish", "english" };

        /// <summary>Every row. Enabled holds the default column.</summary>
        public readonly List<ActionVoiceLine> Lines = new List<ActionVoiceLine>();
        /// <summary>The standard answers of Mission Control, for a line without an answer of its own.</summary>
        public readonly List<ActionVoiceLine> Replies = new List<ActionVoiceLine>();
        /// <summary>The built-in probe beeps (speaker probe). Enabled holds the default column. The rows of a stock
        /// group are what a craft without beeps of its own gets, the custom rows are presets.</summary>
        public readonly List<ProbeBeepLine> Beeps = new List<ProbeBeepLine>();
        public const string SpeakerProbe = "probe";

        public static ActionVoiceLibrary Parse(IList<string> rows, List<string> errors)
        {
            var lib = new ActionVoiceLibrary();
            if (errors == null) errors = new List<string>();
            if (rows == null) { errors.Add("no input"); return lib; }
            int[] col = null;
            int replyEs = -1, replyEn = -1, beepCol = -1;
            for (int i = 0; i < rows.Count; i++)
            {
                string line = (rows[i] ?? "").TrimEnd('\r');
                if (line.Trim().Length == 0 || line.TrimStart().StartsWith("#")) continue;
                string[] f = line.Split('\t');
                for (int k = 0; k < f.Length; k++) f[k] = f[k].Trim();
                if (col == null)
                {
                    col = new int[Columns.Length];
                    for (int k = 0; k < Columns.Length; k++)
                    {
                        col[k] = Array.IndexOf(f, Columns[k]);
                        if (col[k] < 0) { errors.Add("line " + (i + 1) + ": the header has no column " + Columns[k]); return lib; }
                    }
                    replyEs = Array.IndexOf(f, "reply_spanish");
                    replyEn = Array.IndexOf(f, "reply_english");
                    beepCol = Array.IndexOf(f, "beep");
                    continue;
                }
                Func<int, string> get = k => col[k] < f.Length ? f[col[k]] : "";
                if (get(2) == SpeakerProbe)
                {
                    string why = ParseBeep(lib, get(0), get(1), beepCol >= 0 && beepCol < f.Length ? f[beepCol] : "", get(6), get(4),
                                           replyEs >= 0 && replyEs < f.Length ? f[replyEs] : "", replyEn >= 0 && replyEn < f.Length ? f[replyEn] : "");
                    if (why != null) errors.Add("line " + (i + 1) + ": " + why);
                    continue;
                }
                var l = new ActionVoiceLine
                {
                    Group = get(0), State = get(1).Length == 0 ? ActionVoiceLine.StateAny : get(1),
                    Speaker = get(2).Length == 0 ? ActionVoiceLine.SpeakerAuto : get(2), Intercom = get(3) == "intercom",
                    Enabled = get(4) == "on", Spanish = get(5), Text = get(6), BuiltIn = true,
                    ReplySpanish = replyEs >= 0 && replyEs < f.Length ? f[replyEs] : "",
                    Reply = replyEn >= 0 && replyEn < f.Length ? f[replyEn] : "",
                };
                bool isReply = l.Group == ReplyGroup;
                string bad = null;
                int gi = ActionVoiceGroups.IndexOf(l.Group);
                if (isReply) { if (!l.IsMc || l.Enabled) bad = "a reply is said by mc and has default off"; }
                else if (l.Group != CustomGroup && (gi < 0 || ActionVoiceGroups.All[gi].Custom)) bad = "unknown group '" + l.Group + "'";
                else if (l.State != ActionVoiceLine.StateAny && l.State != ActionVoiceLine.StateOn && l.State != ActionVoiceLine.StateOff)
                    bad = "state '" + l.State + "' is not any, on or off";
                else if (l.State != ActionVoiceLine.StateAny && (gi < 0 || !ActionVoiceGroups.All[gi].OnOff))
                    bad = "group '" + l.Group + "' has no on and off";
                else if (l.Speaker != ActionVoiceLine.SpeakerAuto && l.Speaker != ActionVoiceLine.SpeakerMc)
                    bad = "speaker '" + l.Speaker + "' is not auto or mc";
                else if (l.IsMc && l.Intercom) bad = "Mission Control cannot use the intercom";
                else if (l.Text.Length == 0 || l.Spanish.Length == 0) bad = "spanish and english are both needed";
                else if (l.Text.Length > ActionVoiceLine.MaxText || l.Spanish.Length > ActionVoiceLine.MaxText) bad = "a text is longer than " + ActionVoiceLine.MaxText;
                else if (l.Group == CustomGroup && l.Enabled) bad = "a custom preset cannot be on by default";
                if (bad == null)
                {
                    if (l.State != ActionVoiceLine.StateAny && isReply) bad = "a reply has no on and off";
                    else if (l.Text.Length == 0 || l.Spanish.Length == 0) bad = "spanish and english are both needed";
                    else if ((l.Reply.Length == 0) != (l.ReplySpanish.Length == 0)) bad = "reply_spanish and reply_english are both needed";
                    else if (l.Reply.Length > 0 && (isReply || l.IsMc || l.Intercom)) bad = "only a kerbal line on the radio gets a reply";
                    else if (l.Reply.Length > ActionVoiceLine.MaxText || l.ReplySpanish.Length > ActionVoiceLine.MaxText) bad = "a text is longer than " + ActionVoiceLine.MaxText;
                }
                if (bad != null) { errors.Add("line " + (i + 1) + ": " + bad); continue; }
                l.Normalise();
                if (isReply) lib.Replies.Add(l); else lib.Lines.Add(l);
            }
            if (col == null) errors.Add("no header");
            return lib;
        }

        /// <summary>One built-in probe beep: a stock group or custom, a state, a mood in the beep column and the
        /// subtitle [beeps ...] in the english column. The reply columns hold what Mission Control answers.
        /// Null when it is fine, else what is wrong.</summary>
        static string ParseBeep(ActionVoiceLibrary lib, string group, string state, string mood, string english, string dflt,
                                string replySpanish, string reply)
        {
            int gi = ActionVoiceGroups.IndexOf(group);
            bool custom = group == CustomGroup;
            if (!custom && (gi < 0 || ActionVoiceGroups.All[gi].Custom)) return "unknown group '" + group + "'";
            if (state.Length == 0) state = ActionVoiceLine.StateAny;
            if (state != ActionVoiceLine.StateAny && state != ActionVoiceLine.StateOn && state != ActionVoiceLine.StateOff)
                return "state '" + state + "' is not any, on or off";
            if (state != ActionVoiceLine.StateAny && (custom || !ActionVoiceGroups.All[gi].OnOff)) return "group '" + group + "' has no on and off";
            if (Array.IndexOf(ProbeCores.Moods, mood) < 0) return "beep '" + mood + "' is not a probe mood";
            if (!english.StartsWith("[beeps ") || !english.EndsWith("]")) return "a probe beep subtitle looks like [beeps gear down]";
            string words = english.Substring(7, english.Length - 8);
            if (ProbeBeepLine.CleanWords(words) != words) return "a probe beep subtitle has 1 to " + ProbeBeepLine.MaxWords + " lowercase words";
            if (dflt != "on" && dflt != "off") return "default '" + dflt + "' is not on or off";
            if (custom && dflt == "on") return "a custom preset cannot be on by default";
            if ((reply.Length == 0) != (replySpanish.Length == 0)) return "reply_spanish and reply_english are both needed";
            if (reply.Length > ActionVoiceLine.MaxText || replySpanish.Length > ActionVoiceLine.MaxText) return "a text is longer than " + ActionVoiceLine.MaxText;
            if (lib.Beeps.Exists(b => b.Group == group && b.State == state && b.Mood == mood && b.Words == words)) return "the same probe beep twice for " + group + " " + state;
            var n = new ProbeBeepLine { Group = group, State = state, Mood = mood, Words = words, Enabled = dflt == "on",
                                        ReplyOn = true, Reply = reply, ReplySpanish = replySpanish };
            n.Normalise();
            lib.Beeps.Add(n);
            return null;
        }

        /// <summary>The beeps a group offers: its own rows, or the custom rows for a custom group. New copies with
        /// the group filled in.</summary>
        public List<ProbeBeepLine> BeepPresets(ActionGroupDef def)
        {
            var r = new List<ProbeBeepLine>();
            string want = def.Custom ? CustomGroup : def.Key;
            foreach (ProbeBeepLine b in Beeps)
            {
                if (b.Group != want) continue;
                ProbeBeepLine c = b.Clone();
                c.Group = def.Key;
                r.Add(c);
            }
            return r;
        }

        /// <summary>The built-in beeps of the stock groups, new copies: what a craft without beeps of its own gets.</summary>
        public List<ProbeBeepLine> DefaultBeeps()
        {
            var r = new List<ProbeBeepLine>();
            foreach (ProbeBeepLine b in Beeps) if (b.Group != CustomGroup) r.Add(b.Clone());
            return r;
        }

        /// <summary>What Mission Control answers to a probe beep, as a line of its own: the answer of the beep, else
        /// one of the standard answers. Null: no answer.</summary>
        public ActionVoiceLine ReplyFor(ProbeBeepLine beep, Random rng)
        {
            if (beep == null || !beep.ReplyOn) return null;
            if (beep.Reply.Length == 0) return ReplyFor(new ActionVoiceLine { Group = beep.Group, ReplyOn = true }, rng);
            return new ActionVoiceLine { Group = beep.Group, Speaker = ActionVoiceLine.SpeakerMc, Text = beep.Reply,
                                         Spanish = beep.ReplySpanish, BuiltIn = beep.ReplySpanish.Length > 0 };
        }

        /// <summary>The presets a group offers: its own rows, or the custom rows for a custom group. New copies
        /// with the group filled in.</summary>
        public List<ActionVoiceLine> Presets(ActionGroupDef def)
        {
            var r = new List<ActionVoiceLine>();
            string want = def.Custom ? CustomGroup : def.Key;
            foreach (ActionVoiceLine l in Lines)
            {
                if (l.Group != want) continue;
                ActionVoiceLine c = l.Clone();
                c.Group = def.Key;
                r.Add(c);
            }
            return r;
        }

        /// <summary>What Mission Control answers to a line, as a line of its own (Spanish empty: the text goes
        /// through the dictionary). A built-in line gets the answer of its row, a line of the player the answer the
        /// player typed. Without either it is one of the standard answers. Null: no answer.</summary>
        public ActionVoiceLine ReplyFor(ActionVoiceLine line, Random rng)
        {
            if (line == null || !line.WantsReply) return null;
            var r = new ActionVoiceLine { Group = line.Group, Speaker = ActionVoiceLine.SpeakerMc };
            if (line.BuiltIn)
            {
                int gi = ActionVoiceGroups.IndexOf(line.Group);
                string want = gi >= 0 && ActionVoiceGroups.All[gi].Custom ? CustomGroup : line.Group;
                foreach (ActionVoiceLine l in Lines)
                {
                    if (l.Group != want || l.Text != line.Text || l.Reply.Length == 0) continue;
                    r.Text = l.Reply;
                    r.Spanish = l.ReplySpanish;
                    r.BuiltIn = true;
                    return r;
                }
            }
            else if (line.Reply.Length > 0) { r.Text = line.Reply; return r; }
            if (Replies.Count == 0) return null;
            ActionVoiceLine pick = Replies[rng != null ? rng.Next(Replies.Count) : 0];
            r.Text = pick.Text;
            r.Spanish = pick.Spanish;
            r.BuiltIn = true;
            return r;
        }

        /// <summary>What a craft without voicelines of its own gets: every row of the stock groups, switched on or
        /// off by its default column. The custom groups stay empty.</summary>
        public ActionVoiceConfig DefaultConfig()
        {
            var c = new ActionVoiceConfig();
            foreach (ActionVoiceLine l in Lines)
                if (l.Group != CustomGroup) c.Lines.Add(l.Clone());
            c.Beeps.AddRange(DefaultBeeps());
            c.HasBeeps = true;
            return c;
        }
    }

    /// <summary>Picks one line (a voiceline or a probe beep) per activation at random, never the one it picked last
    /// for the same group and state when there is another. It allocates nothing after the first picks.</summary>
    public sealed class ActionVoicePicker<T> where T : class, IActionLine
    {
        readonly T[] last;
        readonly List<T> scratch = new List<T>();

        public ActionVoicePicker(int groups) { last = new T[groups * 2]; }

        /// <summary>group: the index in ActionVoiceGroups.All. valid: an extra test per line (the speaker is there,
        /// the radio works), null for none. Null when no line fits.</summary>
        public T Pick(IList<T> lines, int group, bool on, Predicate<T> valid, Random rng)
        {
            if (lines == null || group < 0 || group * 2 + 1 >= last.Length) return null;
            ActionGroupDef def = ActionVoiceGroups.All[group];
            scratch.Clear();
            for (int i = 0; i < lines.Count; i++)
            {
                T l = lines[i];
                if (l.GroupKey == def.Key && l.Usable && l.Fits(def.OnOff, on) && (valid == null || valid(l))) scratch.Add(l);
            }
            int n = scratch.Count;
            if (n == 0) return null;
            int slot = group * 2 + (def.OnOff && !on ? 1 : 0);
            int skip = n >= 2 && last[slot] != null ? scratch.IndexOf(last[slot]) : -1;
            int pick = rng.Next(skip >= 0 ? n - 1 : n);
            if (skip >= 0 && pick >= skip) pick++;
            T r = scratch[pick];
            last[slot] = r;
            scratch.Clear();
            return r;
        }

        public void Forget() { Array.Clear(last, 0, last.Length); }
    }

    /// <summary>
    /// Turns the state of the action groups into activations. KSP has no event for an action group, but every
    /// activation (key, button, another mod) flips the state of its group exactly once, so a changed state is one
    /// activation. After Reset the first pass only records the states.
    /// </summary>
    public sealed class ActionGroupEdges
    {
        readonly bool[] last;
        bool primed;

        public ActionGroupEdges(int groups) { last = new bool[groups]; }

        /// <summary>A new vessel or a loaded game: the states found next are the start, not activations.</summary>
        public void Reset() { primed = false; }

        /// <summary>Stores the state of one group. True when it changed since the last pass.</summary>
        public bool Update(int group, bool state)
        {
            bool changed = primed && last[group] != state;
            last[group] = state;
            return changed;
        }

        /// <summary>After a pass over every group.</summary>
        public void Prime() { primed = true; }
    }
}
