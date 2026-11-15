// English to Spanish for the voicelines a player types: the voice of Lonapse is Spanish said backwards, so an English
// line becomes Spanish first and loñapsE after. A dictionary (Phrases/english_spanish.tsv), not a real translator:
// the longest listed run of words wins, the rest goes word by word, and the word order stays English. Plurals, -ing
// and -ed of a listed word are worked out. A word that is not listed is said as typed, and a capitalised one in the
// middle of a sentence is taken as a name (RuntimeSpeech.MarkName: said as itself, not reversed).
// Pure code: no Unity or KSP references, so it compiles under DC_OFFLINE_TEST.
using System;
using System.Collections.Generic;
using System.Text;

namespace Lonapse
{
    public sealed class EnglishSpanish
    {
        readonly Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
        int maxWords = 1;

        public int Count { get { return map.Count; } }

        /// <summary>Rows of "english, tab, spanish" under a header with those two columns. # starts a comment.</summary>
        public static EnglishSpanish Parse(IList<string> rows, List<string> errors)
        {
            var d = new EnglishSpanish();
            if (errors == null) errors = new List<string>();
            if (rows == null) { errors.Add("no input"); return d; }
            int en = -1, es = -1;
            for (int i = 0; i < rows.Count; i++)
            {
                string line = (rows[i] ?? "").TrimEnd('\r');
                if (line.Trim().Length == 0 || line.TrimStart().StartsWith("#")) continue;
                string[] f = line.Split('\t');
                for (int k = 0; k < f.Length; k++) f[k] = f[k].Trim();
                if (en < 0)
                {
                    en = Array.IndexOf(f, "english");
                    es = Array.IndexOf(f, "spanish");
                    if (en < 0 || es < 0) { errors.Add("line " + (i + 1) + ": the header needs the columns english and spanish"); return d; }
                    continue;
                }
                if (f.Length <= Math.Max(en, es) || f[en].Length == 0 || f[es].Length == 0) { errors.Add("line " + (i + 1) + ": english and spanish are both needed"); continue; }
                string key = string.Join(" ", f[en].ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
                if (d.map.ContainsKey(key)) { errors.Add("line " + (i + 1) + ": '" + key + "' is listed twice"); continue; }
                d.map[key] = f[es];
                int words = key.Split(' ').Length;
                if (words > d.maxWords) d.maxWords = words;
            }
            if (en < 0) errors.Add("no header");
            return d;
        }

        static bool IsTerminator(char c) { return c == '.' || c == '!' || c == '?' || c == '…'; }

        static bool OnlySpaces(string s)
        {
            if (s.Length == 0) return false;
            foreach (char c in s) if (c != ' ') return false;
            return true;
        }

        static bool EndsSentence(string sep)
        {
            foreach (char c in sep) if (IsTerminator(c) || c == '¡' || c == '¿') return true;
            return false;
        }

        static string UpperFirst(string s)
        {
            return string.IsNullOrEmpty(s) || char.IsUpper(s[0]) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        /// <summary>The text for the voice: Spanish where the dictionary knows the words, with the Spanish opening
        /// marks of a question or an exclamation. Names are marked for RuntimeSpeech.</summary>
        public string Translate(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Replace('’', '\'');
            // seps[i] stands before words[i], the last sep after the last word
            var words = new List<string>();
            var seps = new List<string>();
            var cur = new StringBuilder();
            int p = 0;
            while (p < text.Length)
            {
                char c = text[p];
                bool inWord = char.IsLetter(c) || (c == '\'' && p > 0 && char.IsLetter(text[p - 1]) && p + 1 < text.Length && char.IsLetter(text[p + 1]));
                if (!inWord) { cur.Append(c); p++; continue; }
                seps.Add(cur.ToString());
                cur.Length = 0;
                int start = p;
                while (p < text.Length && (char.IsLetter(text[p]) || (text[p] == '\'' && p + 1 < text.Length && char.IsLetter(text[p + 1])))) p++;
                words.Add(text.Substring(start, p - start));
            }
            seps.Add(cur.ToString());

            var sb = new StringBuilder(text.Length + 16);
            int i = 0;
            while (i < words.Count)
            {
                sb.Append(seps[i]);
                string word = words[i];
                bool start = i == 0 || EndsSentence(seps[i]);
                bool cap = char.IsUpper(word[0]);
                string found = null;
                int used = 1;
                for (int n = Math.Min(maxWords, words.Count - i); n >= 2 && found == null; n--)
                {
                    bool joined = true;
                    for (int k = 1; k < n && joined; k++) joined = OnlySpaces(seps[i + k]);
                    if (!joined) continue;
                    cur.Length = 0;
                    for (int k = 0; k < n; k++) { if (k > 0) cur.Append(' '); cur.Append(words[i + k].ToLowerInvariant()); }
                    string t;
                    if (map.TryGetValue(cur.ToString(), out t)) { found = t; used = n; }
                }
                cur.Length = 0;
                if (found == null) found = Word(word.ToLowerInvariant());
                if (found != null) sb.Append(cap && start ? UpperFirst(found) : found);
                else
                {
                    bool allCaps = word.Length > 1 && word.ToUpperInvariant() == word;
                    // a capital in the middle of a sentence: a name, said as itself. RCS and SAS stay ordinary words.
                    sb.Append(cap && !start && !allCaps ? RuntimeSpeech.MarkName(word) : word);
                }
                i += used;
            }
            sb.Append(seps[seps.Count - 1]);
            return OpeningMarks(sb.ToString());
        }

        /// <summary>One word, or a form of a listed word. Null when the dictionary has nothing.</summary>
        string Word(string w)
        {
            string t;
            if (map.TryGetValue(w, out t)) return t;
            if (w.EndsWith("'s") && map.TryGetValue(w.Substring(0, w.Length - 2), out t)) return t;
            if (w.Length > 4 && w.EndsWith("ing"))
            {
                t = Stem(w.Substring(0, w.Length - 3));
                if (t != null) return Verb(t, "ando", "iendo");
            }
            if (w.Length > 3 && w.EndsWith("ed"))
            {
                t = Stem(w.Substring(0, w.Length - 2)) ?? Lookup(w.Substring(0, w.Length - 1));
                if (t != null) return Verb(t, "ado", "ido");
            }
            if (w.Length > 3 && w.EndsWith("ies") && map.TryGetValue(w.Substring(0, w.Length - 3) + "y", out t)) return Plural(t);
            if (w.Length > 3 && w.EndsWith("es") && map.TryGetValue(w.Substring(0, w.Length - 2), out t)) return Plural(t);
            if (w.Length > 2 && w.EndsWith("s") && map.TryGetValue(w.Substring(0, w.Length - 1), out t)) return Plural(t);
            return null;
        }

        string Lookup(string w) { string t; return map.TryGetValue(w, out t) ? t : null; }

        /// <summary>The verb an -ing or -ed form came from: "deploy", "releas" + e, "stopp" less a p.</summary>
        string Stem(string s)
        {
            if (s.Length < 2) return null;
            string t = Lookup(s) ?? Lookup(s + "e");
            if (t == null && s.Length > 2 && s[s.Length - 1] == s[s.Length - 2]) t = Lookup(s.Substring(0, s.Length - 1));
            return t;
        }

        /// <summary>A Spanish infinitive with its ending for -ing or -ed. Anything else stays as listed.</summary>
        static string Verb(string t, string ar, string er)
        {
            if (t.IndexOf(' ') >= 0 || t.Length < 3) return t;
            string end = t.Substring(t.Length - 2), root = t.Substring(0, t.Length - 2);
            if (end == "ar") return root + ar;
            if (end == "er" || end == "ir") return root + er;
            return t;
        }

        static string Plural(string t)
        {
            if (t.IndexOf(' ') >= 0 || t.Length == 0) return t;
            char last = t[t.Length - 1];
            if (last == 's') return t;
            if (last == 'z') return t.Substring(0, t.Length - 1) + "ces";
            return "aeiouáéó".IndexOf(last) >= 0 ? t + "s" : t + "es";
        }

        /// <summary>Spanish opens a question with ¿ and an exclamation with ¡. The voice reads the sentence better
        /// with them, and the built-in lines have them too.</summary>
        static string OpeningMarks(string s)
        {
            var sb = new StringBuilder(s.Length + 4);
            int from = 0, i = 0;
            while (i < s.Length)
            {
                if (!IsTerminator(s[i])) { i++; continue; }
                int end = i;
                char mark = '\0';
                while (end < s.Length && IsTerminator(s[end]))
                {
                    if (mark == '\0' && s[end] == '?') mark = '¿';
                    else if (mark == '\0' && s[end] == '!') mark = '¡';
                    end++;
                }
                int first = from;
                while (first < i && char.IsWhiteSpace(s[first])) first++;
                sb.Append(s, from, first - from);
                if (mark != '\0' && first < i && s[first] != '¡' && s[first] != '¿') sb.Append(mark);
                sb.Append(s, first, end - first);
                from = i = end;
            }
            sb.Append(s, from, s.Length - from);
            return sb.ToString();
        }
    }
}
