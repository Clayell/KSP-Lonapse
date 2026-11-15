// Offline tests for the pure part of the action group voicelines (src/ActionVoicelines.cs): the stored format,
// the shipped phrase table, the random pick and the edge detector. Run with tests/run.sh.
using System;
using System.Collections.Generic;
using Lonapse;

public static class ActionVoicelineTests
{
    static ActionVoiceLine Line(string group, string text, string state = ActionVoiceLine.StateAny, bool enabled = true)
    {
        return new ActionVoiceLine { Group = group, Text = text, State = state, Enabled = enabled };
    }

    public static void Run(Action<bool, string, string> check)
    {
        // ---- the groups ----
        ActionGroupDef[] defs = ActionVoiceGroups.All;
        var bits = new HashSet<int>();
        foreach (ActionGroupDef d in defs) bits.Add(d.Bit);
        check(defs.Length == 16 && bits.Count == 16, "action: six stock groups and ten custom groups, each its own bit", defs.Length + " groups");
        check(defs[ActionVoiceGroups.IndexOf("abort")].Bit == 0x40 && defs[ActionVoiceGroups.IndexOf("gear")].Bit == 0x02
              && defs[ActionVoiceGroups.IndexOf("custom01")].Bit == 0x80 && defs[ActionVoiceGroups.IndexOf("custom10")].Bit == 0x10000,
              "action: the bits are those of KSPActionGroup", null);
        check(defs[ActionVoiceGroups.IndexOf("abort")].Urgent && !defs[ActionVoiceGroups.IndexOf("abort")].OnOff
              && defs[ActionVoiceGroups.IndexOf("gear")].OnOff && !defs[ActionVoiceGroups.IndexOf("custom03")].OnOff,
              "action: only gear, lights, brakes, rcs and sas know on from off", null);

        // ---- the stored format ----
        var cfg = new ActionVoiceConfig();
        cfg.Lines.Add(new ActionVoiceLine { Group = "abort", Text = "Abort; now | 100% {x} = \"yes\" // ¡ñ!", Speaker = ActionVoiceLine.SpeakerKerbal,
                                            Kerbal = "Valentina Kerman", Intercom = true, Enabled = false });
        cfg.Lines.Add(new ActionVoiceLine { Group = "gear", State = ActionVoiceLine.StateOff, Text = "Gear up.", Spanish = "Tren arriba.", BuiltIn = true });
        cfg.Lines[0].Normalise();
        string data = cfg.Encode();
        bool safe = data.IndexOf('\n') < 0 && data.IndexOf('{') < 0 && data.IndexOf('}') < 0 && data.IndexOf('=') < 0
                    && data.IndexOf("//", StringComparison.Ordinal) < 0 && data.IndexOf('"') < 0 && data == data.Trim();
        check(safe, "action: the stored text holds nothing a craft or save file reads as syntax", data);
        ActionVoiceConfig back = ActionVoiceConfig.Decode(data);
        check(back != null && back.Lines.Count == 2 && back.Lines[0].Text == cfg.Lines[0].Text && back.Lines[0].Kerbal == "Valentina Kerman"
              && back.Lines[0].Intercom && !back.Lines[0].Enabled && !back.Lines[0].BuiltIn && back.Lines[0].Speaker == ActionVoiceLine.SpeakerKerbal
              && back.Lines[1].State == ActionVoiceLine.StateOff && back.Lines[1].Spanish == "Tren arriba." && back.Lines[1].BuiltIn && back.Lines[1].Enabled,
              "action: a config comes back as it was stored", back != null && back.Lines.Count > 0 ? back.Lines[0].Text : "null");
        check(back != null && back.Encode() == data, "action: storing it again gives the same text", null);
        check(ActionVoiceConfig.Decode("") == null && ActionVoiceConfig.Decode(null) == null && ActionVoiceConfig.Decode("nonsense") == null
              && ActionVoiceConfig.Decode("0;") == null,
              "action: nothing stored or unreadable data means the craft follows the library", null);
        ActionVoiceConfig empty = ActionVoiceConfig.Decode(new ActionVoiceConfig().Encode());
        check(empty != null && empty.Lines.Count == 0, "action: a craft with every line removed stays empty, the defaults do not come back", null);
        ActionVoiceConfig newer = ActionVoiceConfig.Decode("2;gear|on|auto||r|1|0|Down|Abajo|extra;warp|any|auto||r|1|0|Go|;short|row");
        check(newer != null && newer.Lines.Count == 1 && newer.Lines[0].Text == "Down",
              "action: a newer version with an extra field reads, an unknown group and a short row are skipped", newer != null ? newer.Lines.Count.ToString() : "null");

        // ---- one line ----
        var mcLine = new ActionVoiceLine { Group = "abort", Speaker = ActionVoiceLine.SpeakerMc, Intercom = true, Text = "  Get   out\tnow ‹x› {y}|  " };
        mcLine.Normalise();
        check(!mcLine.Intercom && mcLine.Text == "Get out now x y", "action: Mission Control never uses the intercom, the text is cleaned", "[" + mcLine.Text + "]");
        var noName = new ActionVoiceLine { Group = "abort", Speaker = ActionVoiceLine.SpeakerKerbal, Kerbal = "", Text = "x" };
        noName.Normalise();
        check(noName.Speaker == ActionVoiceLine.SpeakerAuto, "action: a kerbal line without a name is said by any kerbal", noName.Speaker);
        check(ActionVoiceLine.Filter(new string('a', 500)).Length == ActionVoiceLine.MaxText && ActionVoiceLine.Filter("a b ") == "a b ",
              "action: typing is capped at the longest text and keeps its spaces", null);
        check(!Line("gear", "   ").Usable && !Line("gear", "x", ActionVoiceLine.StateAny, false).Usable && Line("gear", "x").Usable,
              "action: an empty or disabled line is never picked", null);
        check(Line("gear", "x", ActionVoiceLine.StateOn).Fits(true, true) && !Line("gear", "x", ActionVoiceLine.StateOn).Fits(true, false)
              && Line("gear", "x").Fits(true, false) && Line("abort", "x", ActionVoiceLine.StateOn).Fits(false, false),
              "action: an on line fits switching on, a line for both fits both, a group without on and off takes every line", null);
        check(new ActionVoiceLine { Text = "Gear down.", Spanish = "Tren abajo." }.Spoken == "Tren abajo." && new ActionVoiceLine { Text = "My line" }.Spoken == "My line",
              "action: a built-in line is said in its Spanish, a typed line as typed", null);
        // the voice text goes through the loñapsE step exactly once (RuntimeVoice.Request does it)
        check(RuntimeSpeech.Spoken("Gear down!") == "raeG nwod!" && RuntimeSpeech.Spoken(RuntimeSpeech.Spoken("Gear down!")) == "Gear down!",
              "action: one loñapsE step reverses the words and keeps the punctuation, a second would undo it", RuntimeSpeech.Spoken("Gear down!"));

        // ---- the shipped library ----
        var errors = new List<string>();
        string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            "../../GameData/Lonapse/Phrases/action_groups.tsv"));
        ActionVoiceLibrary lib = ActionVoiceLibrary.Parse(System.IO.File.ReadAllLines(path, System.Text.Encoding.UTF8), errors);
        check(errors.Count == 0 && lib.Lines.Count >= 40, "action: the shipped table loads cleanly", errors.Count > 0 ? errors[0] : lib.Lines.Count + " lines");
        ActionVoiceConfig def = lib.DefaultConfig();
        bool stockOnly = true, abortOn = false, sasOff = true;
        foreach (ActionVoiceLine l in def.Lines)
        {
            int gi = ActionVoiceGroups.IndexOf(l.Group);
            if (gi < 0 || defs[gi].Custom) stockOnly = false;
            if (l.Group == "abort" && l.Enabled) abortOn = true;
            if ((l.Group == "sas" || l.Group == "rcs" || l.Group == "brakes") && l.Enabled) sasOff = false;
        }
        check(stockOnly && abortOn && sasOff && def.Count("gear") >= 4 && def.Count("custom01") == 0,
              "action: the defaults give the stock groups lines, leave the custom groups empty and brakes, RCS and SAS switched off", null);
        check(lib.Presets(defs[ActionVoiceGroups.IndexOf("custom07")]).Count >= 10 && lib.Presets(defs[ActionVoiceGroups.IndexOf("custom07")])[0].Group == "custom07"
              && lib.Presets(defs[ActionVoiceGroups.IndexOf("lights")]).Count == def.Count("lights"),
              "action: a custom group offers the generic presets under its own key", null);
        ActionVoiceConfig shipped = ActionVoiceConfig.Decode(def.Encode());
        check(shipped != null && shipped.Lines.Count == def.Lines.Count && shipped.Encode() == def.Encode(),
              "action: the defaults survive being stored in a craft", def.Encode().Length + " characters");
        var bad = new List<string>();
        // ---- the answers of Mission Control ----
        var rr = new Random(5);
        check(lib.Replies.Count >= 3, "action: the table has standard replies", lib.Replies.Count + " replies");
        bool allAnswered = true, noneForMc = true;
        foreach (ActionVoiceLine l in lib.DefaultConfig().Lines)
        {
            ActionVoiceLine a = lib.ReplyFor(l, rr);
            if (l.IsMc) { if (a != null) noneForMc = false; }
            else if (a == null || !a.IsMc || a.Text.Length == 0 || a.Spanish.Length == 0) allAnswered = false;
        }
        check(allAnswered, "action: every built-in kerbal line gets a reply with Spanish", null);
        check(noneForMc, "action: Mission Control does not reply to itself", null);
        ActionVoiceLine gearDown = lib.Presets(ActionVoiceGroups.All[ActionVoiceGroups.IndexOf("gear")])[0];
        check(lib.ReplyFor(gearDown, rr).Text == "Copy, gear deployed.", "action: a built-in line gets the reply of its row", lib.ReplyFor(gearDown, rr).Text);
        ActionVoiceLine preset = lib.Presets(ActionVoiceGroups.All[ActionVoiceGroups.IndexOf("custom04")])[0];
        check(lib.ReplyFor(preset, rr) != null && lib.ReplyFor(preset, rr).BuiltIn, "action: a preset gets a standard reply", null);
        var mine = new ActionVoiceLine { Group = "custom01", Text = "Opening the doors." };
        check(lib.ReplyFor(mine, rr) == null, "action: a custom line with the reply off gets none", null);
        mine.ReplyOn = true;
        check(lib.ReplyFor(mine, rr) != null && lib.ReplyFor(mine, rr).Spanish.Length > 0, "action: a custom line with an empty reply gets a standard one", null);
        mine.Reply = "Doors look good.";
        ActionVoiceLine typedReply = lib.ReplyFor(mine, rr);
        check(typedReply.Text == "Doors look good." && typedReply.Spanish.Length == 0 && !typedReply.BuiltIn,
              "action: a typed reply is used and goes through the dictionary", null);
        mine.Intercom = true;
        check(lib.ReplyFor(mine, rr) == null, "action: no reply to a line on the intercom", null);
        mine.Intercom = false;
        var one = new ActionVoiceConfig();
        one.Lines.Add(mine);
        ActionVoiceLine stored = ActionVoiceConfig.Decode(one.Encode()).Lines[0];
        check(stored.ReplyOn && stored.Reply == "Doors look good.", "action: the reply of a custom line is stored in the craft", one.Encode());
        ActionVoiceLine old = ActionVoiceConfig.Decode("1;custom01|any|auto||r|1|0|Hello there.|").Lines[0];
        check(!old.ReplyOn && old.Reply.Length == 0 && old.Text == "Hello there.", "action: data without reply fields still reads", null);

        ActionVoiceLibrary.Parse(new List<string> {
            "group\tstate\tspeaker\tmode\tdefault\tspanish\tenglish",
            "# a comment\t\t\t\t\t\t",
            "warp\tany\tauto\tradio\ton\tVamos\tGo",
            "abort\ton\tauto\tradio\ton\tVamos\tGo",
            "gear\ton\tmc\tintercom\ton\tVamos\tGo",
            "custom\tany\tauto\tradio\ton\tVamos\tGo",
            "gear\ton\tauto\tradio\ton\t\tGo",
            "gear\ton\tauto\tradio\ton\tVamos\tGo" }, bad);
        check(bad.Count == 5, "action: a bad table row is reported and skipped", bad.Count + " errors");

        // ---- probe beeps ----
        int gearIx = ActionVoiceGroups.IndexOf("gear"), abortIx = ActionVoiceGroups.IndexOf("abort");
        int brakesIx = ActionVoiceGroups.IndexOf("brakes"), c3Ix = ActionVoiceGroups.IndexOf("custom03");
        var beepPick = new ActionVoicePicker<ProbeBeepLine>(defs.Length);
        var br = new Random(5);
        check(def.HasBeeps && def.Beeps.Count == 11 && lib.Beeps.Count == 15
              && beepPick.Pick(def.Beeps, gearIx, true, null, br).Subtitle == "[beeps gear down]"
              && beepPick.Pick(def.Beeps, gearIx, false, null, br).Mood == "proud" && beepPick.Pick(def.Beeps, abortIx, true, null, br).Mood == "scared"
              && beepPick.Pick(def.Beeps, brakesIx, true, null, br) == null && beepPick.Pick(def.Beeps, c3Ix, true, null, br) == null,
              "action beeps: a craft that was never set up beeps for Abort, Gear and Lights and nothing else", lib.Beeps.Count + " beeps");
        check(lib.BeepPresets(defs[brakesIx]).Count == 2 && !lib.BeepPresets(defs[brakesIx])[0].Enabled
              && lib.BeepPresets(defs[c3Ix]).Count == 4 && lib.BeepPresets(defs[c3Ix])[0].Group == "custom03",
              "action beeps: Brakes has beeps that are switched off, and a custom group gets the custom presets", null);
        ProbeBeepLine gearBeep = lib.BeepPresets(defs[gearIx])[0];
        ActionVoiceLine beepReply = lib.ReplyFor(gearBeep, rr);
        check(beepReply != null && beepReply.IsMc && beepReply.Text == "Copy, gear deployed." && beepReply.Spanish == "Recibido, tren desplegado.",
              "action beeps: a built-in beep gets the reply of its row", beepReply != null ? beepReply.Text : "none");
        ProbeBeepLine presetBeep = lib.BeepPresets(defs[c3Ix])[0];
        check(lib.ReplyFor(presetBeep, rr) != null && lib.ReplyFor(presetBeep, rr).BuiltIn, "action beeps: a beep with an empty reply gets a standard one", null);
        presetBeep.Reply = "Doors look good.";
        ActionVoiceLine typedBeepReply = lib.ReplyFor(presetBeep, rr);
        check(typedBeepReply.Text == "Doors look good." && typedBeepReply.Spanish.Length == 0 && !typedBeepReply.BuiltIn,
              "action beeps: a typed reply is used and goes through the dictionary", null);
        presetBeep.ReplyOn = false;
        check(lib.ReplyFor(presetBeep, rr) == null, "action beeps: a beep with the reply off gets none", null);

        var beepCfg = new ActionVoiceConfig { HasBeeps = true };
        beepCfg.Lines.Add(Line("gear", "Down."));
        var gb = new ProbeBeepLine { Group = "gear", State = ActionVoiceLine.StateOn, Mood = "curious", Words = "Wheels  DOWN now please!", ReplyOn = false };
        gb.Normalise();
        var gb2 = new ProbeBeepLine { Group = "gear", State = ActionVoiceLine.StateOn, Mood = "happy", Reply = "Nice  wheels; ¡ñ!" };
        gb2.Normalise();
        var gb3 = new ProbeBeepLine { Group = "gear", State = ActionVoiceLine.StateOn, Mood = "bored", Enabled = false };
        var cb = new ProbeBeepLine { Group = "custom03", Mood = "offended", Reply = "Copy.", ReplySpanish = "Recibido." };
        beepCfg.Beeps.Add(gb); beepCfg.Beeps.Add(gb2); beepCfg.Beeps.Add(gb3); beepCfg.Beeps.Add(cb);
        check(gb.Words == "wheels down now" && gb.Subtitle == "[beeps wheels down now]" && cb.Subtitle == "[beeps indignantly]",
              "action beeps: the subtitle has 1 to 3 lowercase words, or the word of the mood", gb.Subtitle + " " + cb.Subtitle);
        string beepData = beepCfg.Encode();
        ActionVoiceConfig beepBack = ActionVoiceConfig.Decode(beepData);
        check(beepBack != null && beepBack.HasBeeps && beepBack.Lines.Count == 1 && beepBack.Beeps.Count == 4 && beepBack.Encode() == beepData
              && beepBack.BeepCount("gear") == 3 && beepBack.Beeps[0].Mood == "curious" && !beepBack.Beeps[0].ReplyOn
              && beepBack.Beeps[1].ReplyOn && beepBack.Beeps[1].Reply == "Nice wheels; ¡ñ!" && beepBack.Beeps[1].ReplySpanish.Length == 0
              && !beepBack.Beeps[2].Enabled && beepBack.Beeps[3].Reply == "Copy." && beepBack.Beeps[3].ReplySpanish == "Recibido.",
              "action beeps: several beeps per group, with their replies, are stored in the craft and read back the same", beepData);
        bool beepRepeat = false, beepWrong = false;
        var beepSeen = new HashSet<string>();
        ProbeBeepLine beepPrev = null;
        for (int i = 0; i < 200; i++)
        {
            ProbeBeepLine p = beepPick.Pick(beepBack.Beeps, gearIx, true, null, br);
            if (p == null || !p.Enabled || p.Group != "gear") beepWrong = true;
            if (p != null && ReferenceEquals(p, beepPrev)) beepRepeat = true;
            if (p != null) beepSeen.Add(p.Mood);
            beepPrev = p;
        }
        check(!beepWrong && !beepRepeat && beepSeen.Count == 2 && beepPick.Pick(beepBack.Beeps, gearIx, false, null, br) == null,
              "action beeps: one of the beeps that are switched on is picked, never the same twice in a row", beepSeen.Count + " beeps seen");
        var onlyBeeps = new ActionVoiceConfig { HasBeeps = true };
        onlyBeeps.Beeps.Add(new ProbeBeepLine { Group = "abort", Mood = "scared" });
        ActionVoiceConfig onlyBack = ActionVoiceConfig.Decode(onlyBeeps.Encode());
        check(onlyBack != null && onlyBack.Lines.Count == 0 && onlyBack.Beeps.Count == 1 && onlyBack.Encode().StartsWith("1;probe|"),
              "action beeps: a craft with only beeps stores them", onlyBeeps.Encode());
        ActionVoiceConfig before = ActionVoiceConfig.Decode("1;gear|on|auto||r|1|0|Down.|");
        check(before != null && !before.HasBeeps && before.Beeps.Count == 0 && before.Lines.Count == 1,
              "action beeps: data from before the beeps has none, so a flight uses the built-in ones", null);
        ActionVoiceConfig odd = ActionVoiceConfig.Decode("1;probe|warp|any|happy|x|1;probe|gear|off|loud|x|1;probe|gear|on|happy||1;probe|gear|on||x|0");
        check(odd != null && odd.HasBeeps && odd.Beeps.Count == 1 && odd.Beeps[0].Mood == "happy" && odd.Beeps[0].Enabled && odd.Beeps[0].ReplyOn
              && odd.Beeps[0].Reply.Length == 0,
              "action beeps: an unknown group and a beep without a mood are skipped, and a row of six fields still reads", odd.Encode());
        ActionVoiceConfig cleared = ActionVoiceConfig.Decode("1;probe|gear|on||x|0");
        check(cleared != null && cleared.HasBeeps && cleared.Beeps.Count == 0, "action beeps: a craft whose beeps were all removed stays without beeps", null);
        var badBeeps = new List<string>();
        ActionVoiceLibrary badLib = ActionVoiceLibrary.Parse(new List<string> {
            "group\tstate\tspeaker\tmode\tdefault\tspanish\tenglish\treply_spanish\treply_english\tbeep",
            "custom01\tany\tprobe\tradio\ton\t\t[beeps hello]\t\t\thappy",
            "gear\ton\tprobe\tradio\ton\t\t[beeps hello]\t\t\tloud",
            "gear\ton\tprobe\tradio\ton\t\t[Beeps Hello]\t\t\thappy",
            "gear\ton\tprobe\tradio\tmaybe\t\t[beeps hello]\t\t\thappy",
            "abort\ton\tprobe\tradio\ton\t\t[beeps hello]\t\t\thappy",
            "gear\ton\tprobe\tradio\ton\t\t[beeps one two three four]\t\t\thappy",
            "custom\tany\tprobe\tradio\ton\t\t[beeps hello]\t\t\thappy",
            "gear\ton\tprobe\tradio\ton\t\t[beeps hello]\t\tCopy.\thappy",
            "gear\ton\tprobe\tradio\ton\t\t[beeps hello]\t\t\thappy",
            "gear\ton\tprobe\tradio\ton\t\t[beeps hello]\t\t\thappy",
            "gear\ton\tprobe\tradio\toff\t\t[beeps again]\tRecibido.\tCopy.\thappy" }, badBeeps);
        check(badBeeps.Count == 9 && badLib.Beeps.Count == 2, "action beeps: a bad beep row of the table is reported and skipped", badBeeps.Count + " errors");

        // ---- English to Spanish for typed lines ----
        var dictErrors = new List<string>();
        string dictPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            "../../GameData/Lonapse/Phrases/english_spanish.tsv"));
        EnglishSpanish tr = EnglishSpanish.Parse(System.IO.File.ReadAllLines(dictPath, System.Text.Encoding.UTF8), dictErrors);
        check(dictErrors.Count == 0 && tr.Count >= 1000, "translate: the shipped dictionary loads cleanly", dictErrors.Count > 0 ? dictErrors[0] : tr.Count + " entries");
        Action<string, string, string> same = (english, spanish, name) => check(tr.Translate(english) == spanish, name, "\"" + tr.Translate(english) + "\"");
        same("Landing gear deployed.", "Tren de aterrizaje desplegado.", "translate: the longest listed words win, the capital stays at the start");
        same("Abort! Abort!", "¡Abortar! ¡Abortar!", "translate: an exclamation gets its Spanish opening mark, per sentence");
        same("Are we ready?", "¿Están nosotros listo?", "translate: a question gets its opening mark, the word order stays English");
        same("Mission Control, we have an emergency!", "¡Control de Misión, tenemos una emergencia!", "translate: a listed name keeps its own capitals");
        same("Lights on. RCS off.", "Luces encendidas. RCS desactivado.", "translate: on and off are a state, RCS stays as it is");
        same("Deploying the antennas and jettisoned fairings", "Desplegando las antenas y expulsado cofias", "translate: -ing and -ed of a listed verb, a listed plural");
        same("Stopped the engines, releasing clamps, two batteries", "Parado los motores, soltando clamps, dos baterías",
             "translate: a doubled consonant, a dropped e and a plural are worked out, an unknown word is said as typed");
        same("Ready when you are, Jebediah.", "Listo cuando estás, " + RuntimeSpeech.MarkName("Jebediah") + ".", "translate: an unknown capital in a sentence is a name");
        same("Zorblax engaged", "Zorblax activado", "translate: an unknown first word is not taken as a name");
        same("we’re going to the Mun", "vamos a el " + RuntimeSpeech.MarkName("Mun"), "translate: a curly apostrophe reads as a plain one");
        same("¡Tren desplegado!", "¡Tren desplegado!", "translate: a line that is Spanish already stays as it is");
        same("", "", "translate: nothing gives nothing");
        check(RuntimeSpeech.Spoken(tr.Translate("Gear down, Jebediah!")) == "¡nerT ojaba, Yebedia!",
              "translate: the Spanish becomes loñapsE once, the name is said as itself", RuntimeSpeech.Spoken(tr.Translate("Gear down, Jebediah!")));
        var typed = new ActionVoiceLine { Text = "Gear down." };
        var builtIn = new ActionVoiceLine { Text = "Gear is coming down.", Spanish = "El tren está bajando." };
        check(typed.SpokenWith(tr) == "Tren abajo." && builtIn.SpokenWith(tr) == "El tren está bajando." && typed.SpokenWith(null) == "Gear down.",
              "translate: a typed line goes through the dictionary, a built-in line keeps its Spanish", typed.SpokenWith(tr));
        var twice = new List<string>();
        EnglishSpanish.Parse(new List<string> { "english\tspanish", "gear\ttren", "gear\totro", "lonely\t" }, twice);
        check(twice.Count == 2, "translate: a double or half row of the dictionary is reported", twice.Count + " errors");

        // ---- the pick ----
        int gear = ActionVoiceGroups.IndexOf("gear"), abort = ActionVoiceGroups.IndexOf("abort");
        var lines = new List<ActionVoiceLine> {
            Line("gear", "down 1", ActionVoiceLine.StateOn), Line("gear", "down 2", ActionVoiceLine.StateOn), Line("gear", "down 3", ActionVoiceLine.StateOn),
            Line("gear", "up 1", ActionVoiceLine.StateOff), Line("gear", "off", ActionVoiceLine.StateOn, false),
            Line("abort", "a"), Line("abort", "b"),
        };
        var picker = new ActionVoicePicker<ActionVoiceLine>(defs.Length);
        var rng = new Random(7);
        bool repeat = false, wrong = false;
        var seen = new HashSet<string>();
        ActionVoiceLine prev = null;
        for (int i = 0; i < 300; i++)
        {
            ActionVoiceLine p = picker.Pick(lines, gear, true, null, rng);
            if (p == null || p.State != ActionVoiceLine.StateOn || !p.Enabled) wrong = true;
            if (p != null && ReferenceEquals(p, prev)) repeat = true;
            if (p != null) seen.Add(p.Text);
            prev = p;
        }
        check(!wrong && !repeat && seen.Count == 3, "action: one of the enabled on lines per press, all of them come up, never the same twice in a row", seen.Count + " seen");
        bool onlyUp = true;
        for (int i = 0; i < 20; i++) { ActionVoiceLine p = picker.Pick(lines, gear, false, null, rng); if (p == null || p.Text != "up 1") onlyUp = false; }
        check(onlyUp, "action: with one line that fits, that line every time", null);
        bool alternates = true;
        prev = null;
        for (int i = 0; i < 40; i++)
        {
            // Abort flips its state on every press: both states pick from the same lines and share the memory
            ActionVoiceLine p = picker.Pick(lines, abort, i % 2 == 0, null, rng);
            if (p == null || ReferenceEquals(p, prev)) alternates = false;
            prev = p;
        }
        check(alternates, "action: two lines for Abort take turns, whatever state the group is in", null);
        check(picker.Pick(lines, ActionVoiceGroups.IndexOf("lights"), true, null, rng) == null
              && picker.Pick(lines, gear, true, l => false, rng) == null && picker.Pick(null, gear, true, null, rng) == null,
              "action: no line, or none that can be spoken now, picks nothing", null);
        ActionVoiceLine onlyValid = picker.Pick(lines, gear, true, l => l.Text == "down 2", rng);
        check(onlyValid != null && onlyValid.Text == "down 2" && ReferenceEquals(picker.Pick(lines, gear, true, l => l.Text == "down 2", rng), onlyValid),
              "action: the one line that can be spoken is picked again, the no-repeat rule needs two", null);

        // ---- the edges ----
        var edges = new ActionGroupEdges(defs.Length);
        bool first = edges.Update(gear, true) || edges.Update(abort, false);
        edges.Prime();
        bool still = edges.Update(gear, true) || edges.Update(abort, false);
        edges.Prime();
        int fired = 0;
        for (int frame = 0; frame < 50; frame++)
        {
            // the gear stays up from frame 10 on: one change, however long the state lasts
            if (edges.Update(gear, frame < 10)) fired++;
            edges.Prime();
        }
        check(!first && !still && fired == 1, "action: the states found first are no activation, a held state fires once", fired + " fired");
        bool a1 = edges.Update(abort, true); edges.Prime();
        bool a2 = edges.Update(abort, false); edges.Prime();
        check(a1 && a2, "action: every flip is one activation, on and off alike", null);
        edges.Reset();
        bool afterReset = edges.Update(abort, true) || edges.Update(gear, true);
        edges.Prime();
        check(!afterReset && !edges.Update(abort, true), "action: after a vessel switch the new states are the start", null);
    }
}
