// Copyright 2008-2009 Louis DeJardin - http://whereslou.com
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// 
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using Microsoft.CSharp;

namespace Spark.Compiler
{
    public class BatchCompiler
    {
        public string OutputAssembly { get; set; }

        public Assembly Compile(bool debug, string languageOrExtension, params string[] sourceCode)
        {
            var language = languageOrExtension;
            if (CodeDomProvider.IsDefinedLanguage(languageOrExtension) == false &&
                CodeDomProvider.IsDefinedExtension(languageOrExtension))
            {
                language = CodeDomProvider.GetLanguageFromExtension(languageOrExtension);
            }

        	CodeDomProvider codeProvider;
            CompilerParameters compilerParameters;
            
            if (ConfigurationManager.GetSection("system.codedom") != null)
            {
                var compilerInfo = CodeDomProvider.GetCompilerInfo(language);
                codeProvider = compilerInfo.CreateProvider();
                compilerParameters = compilerInfo.CreateDefaultCompilerParameters();
            }
            else
            {
                if (!language.Equals("c#", StringComparison.OrdinalIgnoreCase) && 
                    !language.Equals("cs", StringComparison.OrdinalIgnoreCase) && 
                    !language.Equals("csharp", StringComparison.OrdinalIgnoreCase))
                {
                    throw new CompilerException(
                        string.Format("When running the {0} in an AppDomain without a system.codedom config section only the csharp language is supported. This happens if you are precompiling your views.", 
                            typeof(BatchCompiler).FullName));
                }

                var providerOptions = new Dictionary<string, string> { { "CompilerVersion", "v3.5" } };
                codeProvider = new CSharpCodeProvider(providerOptions);
                compilerParameters = new CompilerParameters();

				// Note: Could make a map of compiler info objects (to support vb) and rewrite the following uncommented code.
                //compilerParameters = new CompilerParameters { WarningLevel = 4 };
                //var compilerInfo = GetCompilerInfoWithoutReadingConfig(compilerParameters);
                //codeProvider = CreateProviderForCSharpV3(compilerInfo);
            }

            var extension = codeProvider.FileExtension;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly is AssemblyBuilder)
                    continue;

                string location;
                try
                {
                    location = assembly.Location;
                }
                catch (NotSupportedException)
                {
                    continue;
                }

                bool assemblyAlreadyReferenced = false;
                string currentAssembly = Path.GetFileName(location);
                foreach (string alreadyAddedAssembly in compilerParameters.ReferencedAssemblies)
                {
                   if (currentAssembly == Path.GetFileName(alreadyAddedAssembly))
                      assemblyAlreadyReferenced = true;
                }

                if (string.IsNullOrEmpty(location) == false && !assemblyAlreadyReferenced)
                {
                  compilerParameters.ReferencedAssemblies.Add( location );
                }
            }

            CompilerResults compilerResults;
            var basePath = AppDomain.CurrentDomain.SetupInformation.DynamicBase ?? Path.GetTempPath();
            if (debug)
            {
                compilerParameters.IncludeDebugInformation = true;

                var baseFile = Path.Combine(basePath, Guid.NewGuid().ToString("n"));

                var codeFiles = new List<string>();
                int fileCount = 0;
                foreach (string sourceCodeItem in sourceCode)
                {
                    ++fileCount;
                    var codeFile = baseFile + "-" + fileCount + "." + extension;
                    using (var stream = new FileStream(codeFile, FileMode.Create, FileAccess.Write))
                    {
                        using (var writer = new StreamWriter(stream))
                        {
                            writer.Write(sourceCodeItem);
                        }
                    }
                    codeFiles.Add(codeFile);
                }

                if (!string.IsNullOrEmpty(OutputAssembly))
                {
                    compilerParameters.OutputAssembly = Path.Combine(basePath, OutputAssembly);
                }
                else
                {
                    compilerParameters.OutputAssembly = baseFile + ".dll";
                }
                compilerResults = codeProvider.CompileAssemblyFromFile(compilerParameters, codeFiles.ToArray());
            }
            else
            {
                if (!string.IsNullOrEmpty(OutputAssembly))
                {
                    compilerParameters.OutputAssembly = Path.Combine(basePath, OutputAssembly);
                }
                else
                {
                    // This should result in the assembly being loaded without keeping the file on disk
                    compilerParameters.GenerateInMemory = true;
                }

                compilerResults = codeProvider.CompileAssemblyFromSource(compilerParameters, sourceCode);
            }

            if (compilerResults.Errors.Count != 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine("Dynamic view compilation failed.");

                foreach (CompilerError err in compilerResults.Errors)
                {
                    sb.AppendFormat("{4}({0},{1}): {2} {3}: ", err.Line, err.Column, err.IsWarning ? "warning" : "error", err.ErrorNumber, err.FileName);
                    sb.AppendLine(err.ErrorText);
                }

                sb.AppendLine();
                foreach (var sourceCodeItem in sourceCode)
                {
                    using (var reader = new StringReader(sourceCodeItem))
                    {
                        for (int lineNumber = 1; ; ++lineNumber)
                        {
                            var line = reader.ReadLine();
                            if (line == null)
                                break;
                            sb.Append(lineNumber).Append(' ').AppendLine(line);
                        }
                    }
                }
                throw new CompilerException(sb.ToString());
            }

            return compilerResults.CompiledAssembly;
        }

        //private static CodeDomProvider CreateProviderForCSharpV3(CompilerInfo compilerInfo)
        //{
        //    CodeDomProvider codeProvider;
        //    var providerOptions = new Dictionary<string, string> { { "CompilerVersion", "v3.5" } };
        //    codeProvider = compilerInfo.CreateProvider(providerOptions);
        //    return codeProvider;
        //}

        //private static CompilerInfo GetCompilerInfoWithoutReadingConfig(CompilerParameters compilerParameters)
        //{
        //    var codeDomProviderTypeName = typeof(CSharpCodeProvider).AssemblyQualifiedName;
        //    var compilerLanguages = new[] { "c#", "cs", "csharp" };
        //    var compilerExtensions = new[] { ".cs" };
        //    var compilerInfo = CompilerInfoExtensions.CreateCompilerInfo(compilerParameters, codeDomProviderTypeName, compilerLanguages, compilerExtensions);
        //    return compilerInfo;
        //}
    }

    //public static class CompilerInfoExtensions
    //{
    //    public static CompilerInfo CreateCompilerInfo(CompilerParameters compilerParams, string codeDomProviderTypeName, 
    //        string[] compilerLanguages, string[] compilerExtensions)
    //    {
    //        var constructor = typeof (CompilerInfo).GetConstructor(
    //            BindingFlags.NonPublic, null, new[] { typeof (CompilerParameters), typeof (string), typeof (string[]), typeof (string[]) }, null);
            
    //        if (constructor == null)
    //        {
    //            return null;
    //        }

    //        return (CompilerInfo) constructor.Invoke(new object[] {compilerParams, codeDomProviderTypeName, compilerLanguages, compilerExtensions});
    //    }

    //    public static CodeDomProvider CreateProvider(this CompilerInfo compilerInfo, IDictionary<string, string> providerOptions)
    //    {
    //        if (providerOptions.Count > 0)
    //        {
    //            ConstructorInfo constructor = compilerInfo.CodeDomProviderType.GetConstructor(new [] { typeof(IDictionary<string, string>) });
    //            if (constructor != null)
    //            {
    //                return (CodeDomProvider)constructor.Invoke(new object[] { providerOptions });
    //            }
    //        }
    //        return (CodeDomProvider)Activator.CreateInstance(compilerInfo.CodeDomProviderType);
    //    }
    //}
}
