using System;
using System.Collections.Generic;
using System.Linq;
using CrappyRevitModelGenerator.Core;

namespace CrappyRevitModelGenerator.Revit
{
    /// <summary>Outcome of the pre-flight checks. Blockers stop the run; warnings are shown and need acknowledgement.</summary>
    public sealed class SafetyCheckResult
    {
        public List<string> Blockers { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
        public bool CanProceed => Blockers.Count == 0;
        public bool IsWorkshared { get; set; }
        public bool HasUnsavedChanges { get; set; }

        public override string ToString() =>
            string.Join(Environment.NewLine, Blockers.Select(b => "Blocked: " + b).Concat(Warnings.Select(w => "Warning: " + w)));
    }

    /// <summary>
    /// The safety rules from plan sections 5.2 and 10, evaluated before any dialog opens and
    /// again (with the chosen settings) before any transaction starts.
    /// </summary>
    public static class DocumentSafetyGuard
    {
        /// <summary>Checks that only depend on the document, for the command entry point.</summary>
        public static SafetyCheckResult CheckDocument(Document doc)
        {
            var result = new SafetyCheckResult();

            if (doc == null)
            {
                result.Blockers.Add("Open a Revit project document first.");
                return result;
            }

            if (doc.IsFamilyDocument)
                result.Blockers.Add("The active document is a family. The generator works only in project documents.");

            if (doc.IsLinked)
                result.Blockers.Add("The active document is a linked model and cannot be modified.");

            if (doc.IsReadOnly)
                result.Blockers.Add("The active document is read-only. Save a writable copy and try again.");

            // Document.IsModifiable is NOT "can I start a transaction" — per its own API remarks
            // it is true only WHILE a transaction is already open, and is false the rest of the
            // time (including the normal idle state a command starts in). Checking it here would
            // reject every ordinary run, so it is deliberately not checked as a pre-flight gate.

            if (doc.IsWorkshared)
            {
                result.IsWorkshared = true;
                var kind = doc.IsDetached ? "detached workshared" : "workshared (central/local)";
                result.Warnings.Add($"The active document is {kind}. Generated content will be created in the active workset. Enable 'Allow workshared documents' to proceed.");
            }

            if (doc.IsModified)
            {
                result.HasUnsavedChanges = true;
                result.Warnings.Add("The document has unsaved changes. Consider saving (or working in a disposable copy) before generating.");
            }

            return result;
        }

        /// <summary>Checks that depend on the document AND the chosen settings, run right before the run starts.</summary>
        public static SafetyCheckResult CheckRun(Document doc, GenerationSettings settings)
        {
            var result = CheckDocument(doc);
            if (settings == null)
            {
                result.Blockers.Add("No settings were supplied.");
                return result;
            }

            var validation = settings.Validate();
            foreach (var error in validation.Errors) result.Blockers.Add(error);
            foreach (var warning in validation.Warnings) result.Warnings.Add(warning);

            if (result.IsWorkshared && !settings.AllowWorksharedDocument && !settings.DryRun)
                result.Blockers.Add("The document is workshared and 'Allow workshared documents' is not enabled.");

            if (!settings.ConfirmedActiveDocument && !settings.DryRun)
                result.Blockers.Add("Confirm that generated content may be created in the active document.");

            return result;
        }
    }
}
