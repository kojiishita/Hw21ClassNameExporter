namespace Hw21ClassNameExporter
{
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using System;
    using System.Collections.Generic;
    using System.Configuration;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Xml.Linq;

    class ReportData
    {
        public List<ControlData> Controls { get; set; } = new List<ControlData>();
    }

    class ControlData
    {
        public string Section { get; set; } = "";
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public string ClassName { get; set; } = "";
    }

    class Program
    {
        static void Main()
        {
            var exeFolder = AppContext.BaseDirectory;
            
            // クラス情報ファイル
            var inputPath = ConfigurationManager.AppSettings["BaseReportClassInfoPath"];
            if (!File.Exists(inputPath))
            {
                throw new FileNotFoundException($"{inputPath}がみつかりません");
            }

            // 出力先フォルダ
            var outFolder = ConfigurationManager.AppSettings["OutputFolder"];
            if (!Directory.Exists(outFolder))
            {
                throw new FileNotFoundException($"{outFolder}がみつかりません");
            }

            var txtOutputPath = Path.Combine(ConfigurationManager.AppSettings["OutputFolder"], "ReportControls.txt");
            var xmlOutputPath = Path.Combine(ConfigurationManager.AppSettings["OutputFolder"], "ReportControls.xml");

            var outputLines = new List<string>
            {
                "FilePath\tSection\tName\tType\tClassName"
            };

            var structuredOutput = new Dictionary<string, ReportData>();
            

            foreach (var line in File.ReadLines(inputPath))
            {
                var parts = line.Split('\t');
                if (parts.Length < 2) continue;

                var className = parts[0];
                var filePath = parts[1];

                if (!File.Exists(filePath)) continue;

                Console.WriteLine($"処理中: {filePath}");

                var code = File.ReadAllText(filePath);
                var tree = CSharpSyntaxTree.ParseText(code);
                var root = tree.GetRoot();

                var controlTypes = root.DescendantNodes()
                    .OfType<FieldDeclarationSyntax>()
                    .SelectMany(f => f.Declaration.Variables.Select(v => new
                    {
                        Name = v.Identifier.Text,
                        Type = f.Declaration.Type.ToString()
                    }))
                    .ToDictionary(x => x.Name, x => x.Type);

                var classNames = root.DescendantNodes()
                    .OfType<ExpressionStatementSyntax>()
                    .Where(e => e.ToString().Contains(".ClassName ="))
                    .Select(e =>
                    {
                        var match = Regex.Match(e.ToString(), @"this\.(\w+)\.ClassName\s*=\s*""(.*?)""");
                        return match.Success ? (Name: match.Groups[1].Value, ClassName: match.Groups[2].Value) : default;
                    })
                    .Where(x => x != default)
                    .ToDictionary(x => x.Name, x => x.ClassName);

                var sectionControls = new List<(string Section, string Control)>();

                var addRangeCalls = root.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Where(inv => inv.ToString().Contains("Controls.AddRange"));

                foreach (var call in addRangeCalls)
                {
                    var text = call.ToString();
                    var sectionMatch = Regex.Match(text, @"this\.(\w+)\.Controls\.AddRange");
                    if (!sectionMatch.Success) continue;

                    var sectionName = sectionMatch.Groups[1].Value;

                    var controlMatches = Regex.Matches(text, @"this\.(\w+)");
                    foreach (Match m in controlMatches)
                    {
                        var controlName = m.Groups[1].Value;
                        sectionControls.Add((sectionName, controlName));
                    }
                }

                var controls = new List<ControlData>();

                foreach (var (section, control) in sectionControls.Distinct())
                {
                    if (!classNames.TryGetValue(control, out var classNameValue)) continue;

                    controlTypes.TryGetValue(control, out var type);
                    var shortType = type?.Split('.').Last() ?? "";

                    outputLines.Add($"{filePath}\t{section}\t{control}\t{shortType}\t{classNameValue}");
                    controls.Add(new ControlData
                    {
                        Section = section,
                        Name = control,
                        Type = shortType,
                        ClassName = classNameValue
                    });
                }

                if (controls.Any())
                {
                    structuredOutput[className] = new ReportData
                    {
                        Controls = controls
                    };
                }
            }

            // 出力①：テキストファイル
            File.WriteAllLines(txtOutputPath, outputLines, Encoding.UTF8);
            Console.WriteLine($"テキスト出力完了: {txtOutputPath}");

            // 出力②：XMLファイル
            var xml = new XDocument(
                new XDeclaration("1.0", "UTF-8", null),
                new XElement("Reports",
                    structuredOutput.Select(kvp =>
                        new XElement("Report",
                            new XAttribute("ClassName", kvp.Key),
                            kvp.Value.Controls.Select(ctrl =>
                                new XElement("Control",
                                    new XElement("Section", ctrl.Section),
                                    new XElement("Name", ctrl.Name),
                                    new XElement("Type", ctrl.Type),
                                    new XElement("ClassName", ctrl.ClassName)
                                )
                            )
                        )
                    )
                )
            );

            using (var writer = new StreamWriter(xmlOutputPath, false, Encoding.UTF8))
            {
                xml.Save(writer);
            }

            Console.WriteLine($"XML出力完了: {xmlOutputPath}");
        }
    }
}
