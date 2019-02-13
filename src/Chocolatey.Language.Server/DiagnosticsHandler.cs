using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Schema;
using Microsoft.Language.Xml;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using Buffer = Microsoft.Language.Xml.Buffer;
using DiagnosticSeverity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity;

namespace Chocolatey.Language.Server
{
    public class DiagnosticsHandler
    {
        private readonly ILanguageServer _router;
        private readonly BufferManager _bufferManager;
        private static readonly IReadOnlyCollection<string> TemplatedValues = new []
        {
            "__replace",
            "space_separated",
            "tag1"
        };

        public DiagnosticsHandler(ILanguageServer router, BufferManager bufferManager)
        {
            _router = router;
            _bufferManager = bufferManager;
        }

        public void PublishDiagnostics(Uri uri, Buffer buffer)
        {
            var text = buffer.GetText(0, buffer.Length);
            var syntaxTree = Parser.Parse(buffer);
            var textPositions = new TextPositions(text);
            var diagnostics = new List<Diagnostic>();

            diagnostics.AddRange(NuspecDoesNotContainTemplatedValuesRequirement(syntaxTree, textPositions));
            diagnostics.AddRange(NuspecDescriptionLengthValidation(syntaxTree, textPositions));

            _router.Document.PublishDiagnostics(new PublishDiagnosticsParams
            {
                Uri = uri,
                Diagnostics = diagnostics
            });
        }

        /// <summary>
        ///   Handler to validate that no templated values remain in the nuspec.
        /// </summary>
        /// <seealso href="https://github.com/chocolatey/package-validator/blob/master/src/chocolatey.package.validator/infrastructure.app/rules/NuspecDoesNotContainTemplatedValuesRequirement.cs">Package validator requirement for templated values.</seealso>
        private IEnumerable<Diagnostic> NuspecDoesNotContainTemplatedValuesRequirement(XmlDocumentSyntax syntaxTree, TextPositions textPositions)
        {
            foreach (var node in syntaxTree.DescendantNodesAndSelf().OfType<XmlTextSyntax>())
            {
                if (!TemplatedValues.Any(x => node.Value.Contains(x, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var range = textPositions.GetRange(node.Start, node.End);

                yield return new Diagnostic {
                    Message = "Templated value which should be removed",
                    Severity = DiagnosticSeverity.Error,
                    Range = range
                };
            }
        }

        /// <summary>
        ///   Handler to validate the length of description in the package metadata.
        /// </summary>
        /// <seealso href="https://github.com/chocolatey/package-validator/blob/master/src/chocolatey.package.validator/infrastructure.app/rules/DescriptionRequirement.cs">Package validator requirement for description.</seealso>
        /// <seealso href="https://github.com/chocolatey/package-validator/blob/master/src/chocolatey.package.validator/infrastructure.app/rules/DescriptionWordCountMaximum4000Requirement.cs">Package validator maximum length requirement for description.</seealso>
        /// <seealso href="https://github.com/chocolatey/package-validator/blob/master/src/chocolatey.package.validator/infrastructure.app/rules/DescriptionWordCountMinimum30Guideline.cs">Package validator minimum length guideline for description.</seealso>
        private IEnumerable<Diagnostic> NuspecDescriptionLengthValidation(XmlDocumentSyntax syntaxTree, TextPositions textPositions)
        {
            var descriptionElement = syntaxTree.DescendantNodes().OfType<XmlElementSyntax>().FirstOrDefault(x => string.Equals(x.Name, "description", StringComparison.OrdinalIgnoreCase));

            if (descriptionElement == null)
            {
                yield return new Diagnostic {
                    Message = "Description is required. See https://github.com/chocolatey/package-validator/wiki/DescriptionNotEmpty",
                    Severity = DiagnosticSeverity.Error,
                    Range = textPositions.GetRange(0, syntaxTree.End)
                };
                yield break;
            }

            var descriptionLength = descriptionElement.GetContentValue().Trim().Length;

            if (descriptionLength == 0)
            {
                var range = textPositions.GetRange(descriptionElement.StartTag.End, descriptionElement.EndTag.Start);

                yield return new Diagnostic {
                    Message = "Description is required. See https://github.com/chocolatey/package-validator/wiki/DescriptionNotEmpty",
                    Severity = DiagnosticSeverity.Error,
                    Range = range
                };
            }
            else if (descriptionLength <= 30)
            {
                var range = textPositions.GetRange(descriptionElement.StartTag.End, descriptionElement.EndTag.Start);

                yield return new Diagnostic {
                    Message = "Description should be sufficient to explain the software. See https://github.com/chocolatey/package-validator/wiki/DescriptionCharacterCountMinimum",
                    Severity = DiagnosticSeverity.Warning,
                    Range = range
                };
            }
            else if (descriptionLength > 4000)
            {
                var range = textPositions.GetRange(descriptionElement.StartTag.End, descriptionElement.EndTag.Start);

                yield return new Diagnostic {
                    Message = "Description should not exceed 4000 characters. See https://github.com/chocolatey/package-validator/wiki/DescriptionCharacterCountMaximum",
                    Severity = DiagnosticSeverity.Error,
                    Range = range
                };
            }
        }
    }
}
