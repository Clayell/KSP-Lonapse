// Where the action group voicelines of a craft live: one persistent text field on every command part.
// KSP writes a part module into the craft file and into the save, and it only keeps a module the part prefab has.
// So the module is on the prefab of every part with a ModuleCommand: Patches/Lonapse_Voicelines.cfg adds it with
// Module Manager, and VoicelineModuleInstaller adds it to the parts that still lack it when the game starts.
// Every command part of a craft holds the same copy. A new root part, a dropped stage or an undocked lander keeps
// one, and after docking the copy of the part nearest the root counts.
// Not tested in game yet: the prefab fallback without Module Manager.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Lonapse
{
    public class ModuleLonapseVoicelines : PartModule
    {
        /// <summary>ActionVoiceConfig.Encode. Empty: the craft has no voicelines of its own and follows the library.</summary>
        [KSPField(isPersistant = true)] public string voicelines = "";

        public override string GetInfo() { return string.Empty; }   // nothing in the editor part info
        public override string GetModuleDisplayName() { return "Lonapse voicelines"; }

        static ModuleLonapseVoicelines Of(Part p)
        {
            try { return p != null ? p.FindModuleImplementing<ModuleLonapseVoicelines>() : null; }
            catch (Exception) { return null; }
        }

        /// <summary>The stored data of the first part that has some (the parts are listed root first). Empty for none.</summary>
        public static string Find(IList<Part> parts)
        {
            if (parts == null) return "";
            for (int i = 0; i < parts.Count; i++)
            {
                ModuleLonapseVoicelines m = Of(parts[i]);
                if (m != null && !string.IsNullOrEmpty(m.voicelines)) return m.voicelines;
            }
            return "";
        }

        /// <summary>How many parts can hold the voicelines.</summary>
        public static int Carriers(IList<Part> parts)
        {
            int n = 0;
            if (parts != null) for (int i = 0; i < parts.Count; i++) if (Of(parts[i]) != null) n++;
            return n;
        }

        /// <summary>Stores the data in every part that can hold it. Returns how many changed.</summary>
        public static int WriteAll(IList<Part> parts, string data)
        {
            int n = 0;
            if (parts == null) return 0;
            for (int i = 0; i < parts.Count; i++)
            {
                ModuleLonapseVoicelines m = Of(parts[i]);
                if (m == null || m.voicelines == data) continue;
                m.voicelines = data ?? "";
                n++;
            }
            return n;
        }
    }

    /// <summary>The built-in phrase library, read once per game session from Phrases/action_groups.tsv.</summary>
    public static class ActionVoiceDefaults
    {
        public const string FileRel = "GameData/Lonapse/Phrases/action_groups.tsv";
        static ActionVoiceLibrary library;
        static ActionVoiceConfig config;

        public static ActionVoiceLibrary Library
        {
            get
            {
                if (library != null) return library;
                var errors = new List<string>();
                try
                {
                    string path = Path.Combine(KSPUtil.ApplicationRootPath, FileRel);
                    if (File.Exists(path)) library = ActionVoiceLibrary.Parse(File.ReadAllLines(path, System.Text.Encoding.UTF8), errors);
                    else errors.Add("no file at " + path);
                }
                catch (Exception e) { errors.Add("read failed: " + e.Message); }
                if (library == null) library = new ActionVoiceLibrary();
                for (int i = 0; i < errors.Count && i < 10; i++) Log.Warn("Action group voicelines table: " + errors[i]);
                Log.Info("Action group voicelines: " + library.Lines.Count + " built-in lines");
                return library;
            }
        }

        public const string DictionaryRel = "GameData/Lonapse/Phrases/english_spanish.tsv";
        static EnglishSpanish dictionary;

        /// <summary>The English to Spanish dictionary for typed lines. Empty when the file is missing: a typed line
        /// is then said as typed.</summary>
        public static EnglishSpanish Dictionary
        {
            get
            {
                if (dictionary != null) return dictionary;
                var errors = new List<string>();
                try
                {
                    string path = Path.Combine(KSPUtil.ApplicationRootPath, DictionaryRel);
                    if (File.Exists(path)) dictionary = EnglishSpanish.Parse(File.ReadAllLines(path, System.Text.Encoding.UTF8), errors);
                    else errors.Add("no file at " + path);
                }
                catch (Exception e) { errors.Add("read failed: " + e.Message); }
                if (dictionary == null) dictionary = new EnglishSpanish();
                for (int i = 0; i < errors.Count && i < 10; i++) Log.Warn("English to Spanish dictionary: " + errors[i]);
                Log.Info("Action group voicelines: " + dictionary.Count + " English to Spanish entries for typed lines");
                return dictionary;
            }
        }

        /// <summary>What the voice says for a line, before the loñapsE step (ActionVoiceLine.SpokenWith).</summary>
        public static string SpokenFor(ActionVoiceLine line) { return line.SpokenWith(Dictionary); }

        /// <summary>What a craft without voicelines of its own uses. Shared: read it, never change it.</summary>
        public static ActionVoiceConfig Config { get { return config ?? (config = Library.DefaultConfig()); } }
    }

    /// <summary>Without Module Manager (or for a part a later patch gave a command module): adds
    /// ModuleLonapseVoicelines to every part prefab with a ModuleCommand that does not have it yet. At the end of the
    /// module list, so the modules of existing craft and saves keep their places.</summary>
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public sealed class VoicelineModuleInstaller : MonoBehaviour
    {
        private void Start()
        {
            int added = 0, had = 0, failed = 0;
            try
            {
                if (PartLoader.LoadedPartsList == null) return;
                foreach (AvailablePart ap in PartLoader.LoadedPartsList)
                {
                    Part prefab = ap != null ? ap.partPrefab : null;
                    if (prefab == null || prefab.Modules == null) continue;
                    bool command = false, has = false;
                    for (int i = 0; i < prefab.Modules.Count; i++)
                    {
                        PartModule m = prefab.Modules[i];
                        if (m is ModuleCommand) command = true;
                        if (m is ModuleLonapseVoicelines) has = true;
                    }
                    if (!command) continue;
                    if (has) { had++; continue; }
                    try { if (prefab.AddModule(typeof(ModuleLonapseVoicelines).Name, true) != null) added++; else failed++; }
                    catch (Exception e) { failed++; Log.Debug("Voicelines module not added to " + ap.name + ": " + e.Message); }
                }
            }
            catch (Exception e) { Log.Exception("VoicelineModuleInstaller", e); }
            Log.Info("Action group voicelines: " + had + " command parts had the module (Module Manager), " + added + " got it now"
                     + (failed > 0 ? ", " + failed + " failed" : ""));
        }
    }
}
