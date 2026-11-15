// Action group voicelines in flight: sees an action group of the active vessel fire, picks one of the voicelines
// the craft has for it, has Piper speak it and puts it in the queue like any other line.
// KSP has no event for an action group. ActionGroupList.ToggleGroup flips the state of the group, runs the part
// actions and nothing else, and SetGroup only calls it when the state differs. So one activation, by a key, the
// action group buttons or another mod, is one flip of the state, and that flip is what PollActionGroups reads.
// A group nothing on the craft is bound to stays silent. RCS and SAS are switches of the craft itself, so they count
// as bound. A kerbal line on the radio can get an answer from Mission Control, rendered next to the line itself.
// Not seen: two flips of one group inside one frame, and a mod that runs part actions without the group.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lonapse
{
    public partial class ChatterDirector
    {
        /// <summary>After a vessel switch or a load the states settle for this long (ModuleLight writes the Lights
        /// state when its part starts). Nothing in that time is an activation.</summary>
        const float ActionSettle = 2f;
        /// <summary>A line Piper needs longer for than this is dropped, and so is a queued line that waited this long
        /// since the key press. A callout that comes half a minute late is worse than none.</summary>
        const float ActionMaxRender = 6f, ActionMaxAge = 12f;
        /// <summary>Lines on their way to Piper at once. More key presses in that time say nothing.</summary>
        const int ActionMaxPending = 2;

        static int[] actionListIndex;
        readonly ActionGroupEdges actionEdges = new ActionGroupEdges(ActionVoiceGroups.All.Length);
        readonly ActionVoicePicker<ActionVoiceLine> actionPicker = new ActionVoicePicker<ActionVoiceLine>(ActionVoiceGroups.All.Length);
        readonly ActionVoicePicker<ProbeBeepLine> actionBeepPicker = new ActionVoicePicker<ProbeBeepLine>(ActionVoiceGroups.All.Length);
        Predicate<ActionVoiceLine> actionValid;
        Vessel actionVessel;
        float actionSettle;
        int actionPending;
        float actionAsked = -999f;
        string actionData;
        ActionVoiceConfig actionOwn;
        /// <summary>The last action group line and what became of it (the overlay and the log).</summary>
        public string lastActionLine = "";

        /// <summary>The place of each supported group in ActionGroupList.groups.</summary>
        static int[] ActionListIndex
        {
            get
            {
                if (actionListIndex != null) return actionListIndex;
                ActionGroupDef[] defs = ActionVoiceGroups.All;
                var r = new int[defs.Length];
                for (int i = 0; i < defs.Length; i++)
                {
                    try { r[i] = BaseAction.GetGroupIndex((KSPActionGroup)defs[i].Bit); }
                    catch (Exception) { r[i] = -1; }
                }
                return actionListIndex = r;
            }
        }

        /// <summary>Every frame from the addon, also with the gate closed, so the stored states never go stale and a
        /// reopened gate does not read old changes as activations.</summary>
        public void PollActionGroups(Vessel v, float dt)
        {
            ActionGroupList list = null;
            try { if (v != null && v.loaded && !v.isEVA) list = v.ActionGroups; } catch (Exception) { }
            if (list == null || list.groups == null)
            {
                actionVessel = null;
                actionEdges.Reset();
                return;
            }
            if (!ReferenceEquals(v, actionVessel))
            {
                actionVessel = v;
                actionEdges.Reset();
                actionSettle = ActionSettle;
            }
            if (actionSettle > 0f) actionSettle -= dt;
            List<bool> groups = list.groups;
            int[] index = ActionListIndex;
            for (int i = 0; i < index.Length; i++)
            {
                int gi = index[i];
                if (gi < 0 || gi >= groups.Count) continue;
                bool on = groups[gi];
                if (actionEdges.Update(i, on) && actionSettle <= 0f) OnActionGroup(v, i, on);
            }
            actionEdges.Prime();
        }

        /// <summary>The voicelines of the vessel: its own (the command part nearest the root that has some), else the
        /// built-in ones when the player wants those. Only read when an action group fires.</summary>
        ActionVoiceConfig ActionConfigFor(Vessel v)
        {
            string data = ModuleLonapseVoicelines.Find(v.parts);
            if (data != actionData)
            {
                actionData = data;
                actionOwn = ActionVoiceConfig.Decode(data);
                actionPicker.Forget();
                actionBeepPicker.Forget();
                if (data.Length > 0 && actionOwn == null) Log.Warn("Action group voicelines of " + Names.Of(v) + " could not be read, the built-in lines are used");
            }
            if (actionOwn != null) return actionOwn;
            return host.settings.actionVoiceDefaults ? ActionVoiceDefaults.Config : null;
        }

        void OnActionGroup(Vessel v, int group, bool on)
        {
            Settings cfg = host.settings;
            ActionGroupDef def = ActionVoiceGroups.All[group];
            if (!cfg.actionVoicelines || !host.CanStartAudio || host.frozenShip || host.evaMode) return;
            string what = def.Label + (def.OnOff ? (on ? " on" : " off") : "");
            if (!cool.Ready(def.CooldownKey, clock, cfg.actionVoiceCooldown))
            {
                Log.Debug("Action group " + what + ": no line, the group spoke less than " + cfg.actionVoiceCooldown.ToString("0.#") + " s ago");
                return;
            }
            // a line Piper never answered (its process was stopped) must not block the next ones for the whole flight
            if (actionPending > 0 && clock - actionAsked > 3f * ActionMaxRender) actionPending = 0;
            if (actionPending >= ActionMaxPending) { Log.Debug("Action group " + what + ": no line, " + actionPending + " lines are already on their way"); return; }
            if (!ActionGroupBound(v, def))
            {
                lastActionLine = what + ": nothing bound";
                Log.Debug("Action group " + what + ": no line, nothing on the craft is bound to it");
                return;
            }
            ActionVoiceConfig c;
            try { c = ActionConfigFor(v); }
            catch (Exception e) { Log.Exception("ActionConfigFor", e); return; }
            if (c == null) return;
            if (host.probeMode)
            {
                // a probe has no kerbal to speak: one of the beeps of the group, and Mission Control may answer it.
                // A group without a beep still plays its Mission Control lines.
                ProbeBeepLine beep = ActionBeepFor(c, group, on);
                if (beep != null)
                {
                    string why = ActionBeepBlocked();
                    if (why != null) { lastActionLine = what + ": no beep, " + why; Log.Debug("Action group " + what + ": no beep, " + why); return; }
                    cool.Stamp(def.CooldownKey, clock);
                    SpeakActionBeep(v, def, beep, what);
                    return;
                }
            }
            if (actionValid == null) actionValid = ActionLineValid;
            ActionVoiceLine line = actionPicker.Pick(c.Lines, group, on, actionValid, rng);
            if (line == null) { Log.Debug("Action group " + what + ": no line fits now"); return; }
            cool.Stamp(def.CooldownKey, clock);
            SpeakActionLine(v, def, line, what);
        }

        /// <summary>Something on the vessel listens to the group: a part or a module has an action in it. Gear, lights
        /// and wheel brakes are in their group from the start. RCS and SAS switch the craft itself. Only read when a
        /// group fires. Not looked at: the axis groups and the override sets of the action group editor.</summary>
        static bool ActionGroupBound(Vessel v, ActionGroupDef def)
        {
            if (def.Key == "rcs" || def.Key == "sas") return true;
            try
            {
                KSPActionGroup bit = (KSPActionGroup)def.Bit;
                List<Part> parts = v.parts;
                for (int i = 0; i < parts.Count; i++)
                {
                    Part p = parts[i];
                    if (p == null) continue;
                    if (ActionsBound(p.Actions, bit)) return true;
                    for (int m = 0; m < p.Modules.Count; m++)
                        if (ActionsBound(p.Modules[m].Actions, bit)) return true;
                }
                return false;
            }
            catch (Exception e)
            {
                // better a line too many than a feature that went silent
                Log.Exception("ActionGroupBound", e);
                return true;
            }
        }

        static bool ActionsBound(BaseActionList actions, KSPActionGroup bit)
        {
            if (actions == null) return false;
            foreach (BaseAction a in actions) if (a != null && (a.actionGroup & bit) != 0) return true;
            return false;
        }

        /// <summary>One of the beeps of the craft for one activation, picked like a voiceline. Data from before the
        /// beeps uses the built-in ones. Null: no beep fits.</summary>
        ProbeBeepLine ActionBeepFor(ActionVoiceConfig c, int group, bool on)
        {
            if (!c.HasBeeps)
            {
                if (!host.settings.actionVoiceDefaults) return null;
                c = ActionVoiceDefaults.Config;
            }
            return actionBeepPicker.Pick(c.Beeps, group, on, null, rng);
        }

        /// <summary>Why the probe cannot beep now, null when it can. The beep goes over the link like any probe beep.</summary>
        string ActionBeepBlocked()
        {
            if (!host.settings.radioEnabled) return "the radio is off";
            if (!ProbeLink) return "no link";
            if (ProbeAsleep) return "the probe is asleep";
            if (count + 6 > Capacity) return "the queue is full";
            return null;
        }

        /// <summary>The probe beeps for an action group, with the subtitle of the beep. Mission Control answers with
        /// the reply of the beep, or one of its standard replies, when the player wants that.</summary>
        void SpeakActionBeep(Vessel v, ActionGroupDef def, ProbeBeepLine beep, string what)
        {
            float asked = clock;
            ActionReply reply = AskActionReply(def, McCanAnswer() ? ActionVoiceDefaults.Library.ReplyFor(beep, rng) : null, what);
            var e = new PhraseEntry
            {
                ClipKey = "action_beep_" + def.Key, Event = "chatter", Severity = def.Urgent ? "warning" : "chatter",
                Band = def.Urgent ? "tense" : "calm", Role = "probe", Scene = "action_" + def.Key, Seq = 1, Gender = "both",
                Beep = beep.Mood, English = beep.Subtitle, Spanish = "",
            };
            // Abort goes before the chatter, like a warning
            if (def.Urgent) PreemptChatter();
            Utterance u = Enqueue(UttKind.ProbeBeep, null, def.Urgent ? 0f : U(0.05f, 0.25f), 0f, null);
            if (u == null) { DropActionReply(reply); lastActionLine = what + ": no beep, the queue is full"; return; }
            u.entry = e;
            if (reply != null)
            {
                reply.fresh = () => clock - asked < 3f * ActionMaxAge && ReferenceEquals(ActiveVessel, v);
                reply.lineQueued = true;
                if (reply.clip != null) QueueActionReply(reply);
            }
            lastActionLine = what + ": probe " + beep.Subtitle + (reply != null ? ", Mission Control answers" : "");
            Log.Info("Action group " + what + ": probe " + beep.Mood + " " + beep.Subtitle + (reply != null ? ", Mission Control answers" : ""));
        }

        bool McCanAnswer()
        {
            Settings cfg = host.settings;
            return cfg.radioEnabled && !radioSilent && !McUnreachable && host.signal.connected;
        }

        /// <summary>The answer of Mission Control to one line. Piper renders it next to the line, and it is queued
        /// once both are there and the line itself made it into the queue.</summary>
        sealed class ActionReply
        {
            public PhraseEntry entry;
            public AudioClip clip;
            public bool lineQueued, lineGone;
            public Func<bool> fresh;
            public string what;
        }

        PhraseEntry ActionEntry(ActionGroupDef def, bool mc, string template, string english)
        {
            bool urgent = def.Urgent;
            var e = new PhraseEntry
            {
                ClipKey = "action_" + def.Key + "_" + (++runtimeAsked), Event = "chatter", Severity = urgent ? "warning" : "chatter",
                Band = urgent ? "tense" : "calm", Role = mc ? "mc" : "crew", Scene = "action_" + def.Key, Seq = 1,
                Gender = mc ? "m" : "both", Phase = "any", Cause = "any",
                Spanish = RuntimeSpeech.Unmarked(template), LonapseText = RuntimeSpeech.Spoken(template), English = english,
            };
            e.Folder = PhraseTable.FolderFor(e.Role, e.Severity, e.Band, e.Event);
            return e;
        }

        /// <summary>Asks Piper for the answer (a line of Mission Control from ReplyFor). Null when there is none to give.</summary>
        ActionReply AskActionReply(ActionGroupDef def, ActionVoiceLine reply, string what)
        {
            if (reply == null) return null;
            string template = ActionVoiceDefaults.SpokenFor(reply);
            if (template.Trim().Length == 0) return null;
            var rs = new ActionReply { entry = ActionEntry(def, true, template, reply.Text), what = what };
            string kind = RuntimeDelivery.KindFor("mc", rs.entry.Band, rs.entry.Severity, rs.entry.Event);
            if (kind == null || RuntimeVoice.Missing(kind) != null) return null;
            Voice.Request(kind, template, r =>
            {
                if (r.clip == null) { Log.Debug("Action group " + what + ": no answer, " + (r.error ?? "Piper gave no clip")); return; }
                if (rs.lineGone) { UnityEngine.Object.Destroy(r.clip); return; }
                rs.clip = r.clip;
                if (rs.lineQueued) QueueActionReply(rs);
            });
            return rs;
        }

        /// <summary>The line did not make it: its answer goes too.</summary>
        static void DropActionReply(ActionReply rs)
        {
            if (rs == null) return;
            rs.lineGone = true;
            if (rs.clip != null) UnityEngine.Object.Destroy(rs.clip);
            rs.clip = null;
        }

        void QueueActionReply(ActionReply rs)
        {
            AudioClip clip = rs.clip;
            rs.clip = null;
            if (clip == null) return;
            if (!host.CanStartAudio || !rs.fresh() || !McCanAnswer() || count + 4 > Capacity)
            {
                UnityEngine.Object.Destroy(clip);
                Log.Debug("Action group " + rs.what + ": the answer of Mission Control was dropped");
                return;
            }
            PhraseClips.SetRuntimeClip(rs.entry, clip);
            runtimeOnly++;
            int start = count;
            EnqueueMc(rs.entry.Folder, U(0.5f, 1.1f), rs.entry);
            GuardQueued(start, rs.fresh);
            Log.Info("Action group " + rs.what + ": Mission Control answers \"" + rs.entry.English + "\"");
        }

        /// <summary>The line can be spoken now: Mission Control has a link, or a kerbal who can talk is aboard. A probe
        /// never speaks as a kerbal, and a kerbal line never moves to Mission Control.</summary>
        bool ActionLineValid(ActionVoiceLine l)
        {
            Settings cfg = host.settings;
            if (l.IsMc) return cfg.radioEnabled && !radioSilent && !McUnreachable && host.signal.connected;
            if (host.probeMode) return false;
            if (l.Intercom ? !cfg.intercomEnabled : (!cfg.radioEnabled || radioSilent)) return false;
            List<KerbalVoice> list = host.roster.voices;
            for (int i = 0; i < list.Count; i++) if (ActionCanSpeak(list[i])) return true;
            return false;
        }

        /// <summary>Aboard the active vessel and conscious. A kerbal nearby on EVA or in another vessel is not the
        /// crew of this craft.</summary>
        static bool ActionCanSpeak(KerbalVoice k) { return k != null && !k.away && !k.outDueToG; }

        /// <summary>Who says a kerbal line: the chosen kerbal when aboard, else another one (Abort: the pilot first,
        /// like a callout, else somebody who did not just speak). Never a tourist when a crew member can.</summary>
        KerbalVoice ActionSpeaker(ActionVoiceLine l, bool urgent)
        {
            List<KerbalVoice> list = host.roster.voices;
            if (l.Speaker == ActionVoiceLine.SpeakerKerbal)
                for (int i = 0; i < list.Count; i++)
                    if (list[i] != null && list[i].name == l.Kerbal && ActionCanSpeak(list[i])) return list[i];
            if (urgent)
                for (int i = 0; i < list.Count; i++)
                    if (list[i] != null && list[i].isPilot && ActionCanSpeak(list[i])) return list[i];
            KerbalVoice k = KerbalVoice.PickWithTrait(list, null, host.roster.lastSpeaker, rng, x => ActionCanSpeak(x) && !x.persona.IsTourist)
                            ?? KerbalVoice.PickWithTrait(list, null, null, rng, ActionCanSpeak);
            if (k != null) host.roster.lastSpeaker = k;
            return k;
        }

        void SpeakActionLine(Vessel v, ActionGroupDef def, ActionVoiceLine line, string what)
        {
            bool mc = line.IsMc, urgent = def.Urgent, intercom = !mc && line.Intercom;
            KerbalVoice sp = mc ? null : ActionSpeaker(line, urgent);
            if (!mc && sp == null) return;
            string template = ActionVoiceDefaults.SpokenFor(line);
            PhraseEntry e = ActionEntry(def, mc, template, line.Text);
            string kind = RuntimeDelivery.KindFor(mc ? "mc" : KindFor(sp), e.Band, e.Severity, e.Event);
            string who = mc ? "Mission Control" : sp.displayName + (intercom ? ", intercom" : ", radio");
            string missing = kind != null ? RuntimeVoice.Missing(kind) : "no voice for this line";
            if (missing != null)
            {
                lastActionLine = what + ": no runtime voice";
                Log.WarnOnce("action-voice:" + kind, "Action group voicelines need the runtime voice: " + missing);
                return;
            }
            float asked = clock;
            actionAsked = clock;
            actionPending++;
            Log.Info("Action group " + what + ": \"" + line.Text + "\" (" + who + ")"
                     + (line.Spanish.Length == 0 ? ", put into Spanish as \"" + e.Spanish + "\"" : ""));
            ActionReply reply = AskActionReply(def, McCanAnswer() ? ActionVoiceDefaults.Library.ReplyFor(line, rng) : null, what);
            Voice.Request(kind, template, r =>
            {
                actionPending = Math.Max(0, actionPending - 1);
                if (r.clip == null)
                {
                    DropActionReply(reply);
                    runtimeFailed++;
                    lastActionLine = what + ": speech failed";
                    Log.Info("Action group " + what + ": no sound, " + (r.error ?? "Piper gave no clip"));
                    return;
                }
                string drop = !host.CanStartAudio ? "the gate closed"
                              : !ReferenceEquals(ActiveVessel, v) ? "another vessel is active"
                              : clock - asked > ActionMaxRender ? "the speech took too long"
                              : !ActionLineValid(line) ? "its speaker or the radio is gone"
                              : sp != null && !(host.roster.voices.Contains(sp) && ActionCanSpeak(sp)) ? sp.displayName + " cannot speak"
                              : count + 8 > Capacity ? "the queue is full" : null;
                if (drop != null)
                {
                    DropActionReply(reply);
                    UnityEngine.Object.Destroy(r.clip);
                    lastActionLine = what + ": dropped, " + drop;
                    Log.Info("Action group " + what + ": line dropped, " + drop);
                    return;
                }
                PhraseClips.SetRuntimeClip(e, r.clip);
                runtimeOnly++;
                Func<bool> fresh = () => clock - asked < ActionMaxAge && ReferenceEquals(ActiveVessel, v);
                // Abort goes before the chatter, like a warning. A line that is being said on the intercom still ends first.
                if (urgent) PreemptChatter();
                int start = count;
                float delay = urgent ? 0f : U(0.05f, 0.25f);
                if (mc) EnqueueMc(e.Folder, delay, e);
                else if (intercom) QueueIntercomLine(sp, e, delay);
                else EnqueueCrew(e.Folder, delay, 0f, sp, e, fresh);
                GuardQueued(start, fresh);
                if (reply != null)
                {
                    // the answer may come after the whole line was said, so it gets more time than the line itself
                    reply.fresh = () => clock - asked < 3f * ActionMaxAge && ReferenceEquals(ActiveVessel, v);
                    reply.lineQueued = true;
                    if (reply.clip != null) QueueActionReply(reply);
                }
                lastActionLine = what + ": " + who + (reply != null ? ", Mission Control answers" : "");
                Log.Info("Action group " + what + ": queued " + e.ClipKey + " \"" + r.spoken + "\", ready in " + r.queueToReadyMs.ToString("0")
                         + " ms (Piper " + r.piperMs.ToString("0") + " ms)");
            });
        }

        /// <summary>One line on the intercom: click, line, release click, like a turn of a chat. A chat that waits in
        /// the queue makes room. The line ignores the stress limit of a chat, the player asked for it.</summary>
        void QueueIntercomLine(KerbalVoice sp, PhraseEntry e, float delay)
        {
            if (HasChatItems()) DropChat("an action group line");
            if (count + 3 > Capacity) return;
            chatA = chatB = sp;
            chatForced = true;
            chatStarted = false;
            lastChatClock = clock;
            chatLimit = 1f;
            Enqueue(UttKind.IntercomClick, null, delay, 0f, null);
            Utterance u = Enqueue(UttKind.IntercomLine, null, 0.04f, 0f, sp);
            if (u != null) u.entry = e;
            Enqueue(UttKind.IntercomClick, null, 0.1f, 0f, null);
        }
    }
}
