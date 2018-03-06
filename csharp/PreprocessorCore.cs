namespace XMLPreprocessor
{
    using System;
    using System.Collections;
    using System.IO;
    using System.Text;
    using System.Xml;
    public enum Platform
    {
        X86,
        X64,
        IA64,
        ARM
    }

    public sealed class PreprocessorCore
    {
        private static readonly char[] variableSplitter = new char[] { '.' };
        private static readonly char[] argumentSplitter = new char[] { ',' };

        private Platform currentPlatform;
        private bool encounteredError;
        private Hashtable extensionsByPrefix;
        private string sourceFile;
        private Hashtable variables;

        internal PreprocessorCore(string sourceFile, Hashtable variables)
        {
            this.sourceFile = Path.GetFullPath(sourceFile);

            this.variables = new Hashtable();

        }

        public Platform CurrentPlatform
        {
            get { return this.currentPlatform; }
            set { this.currentPlatform = value; }
        }

        public bool EncounteredError
        {
            get { return this.encounteredError; }
        }

        public string PreprocessString(string value)
        {
            StringBuilder sb = new StringBuilder();
            int currentPosition = 0;
            int end = 0;

            while (-1 != (currentPosition = value.IndexOf('$', end)))
            {
                if (end < currentPosition)
                {
                    sb.Append(value, end, currentPosition - end);
                }

                end = currentPosition + 1;
                string remainder = value.Substring(end);
                if (remainder.StartsWith("$", StringComparison.Ordinal))
                {
                    sb.Append("$");
                    end++;
                }
                else if (remainder.StartsWith("(loc.", StringComparison.Ordinal))
                {
                    currentPosition = remainder.IndexOf(')');
                    if (-1 == currentPosition)
                    {
                        throw new Exception("Undefined Preprocessor Variable: " + remainder);
                    }

                    sb.Append("$");   // just put the resource reference back as was
                    sb.Append(remainder, 0, currentPosition + 1);

                    end += currentPosition + 1;
                }
                else if (remainder.StartsWith("(", StringComparison.Ordinal))
                {
                    int openParenCount = 1;
                    int closingParenCount = 0;
                    bool isFunction = false;
                    bool foundClosingParen = false;

                    // find the closing paren
                    int closingParenPosition;
                    for (closingParenPosition = 1; closingParenPosition < remainder.Length; closingParenPosition++)
                    {
                        switch (remainder[closingParenPosition])
                        {
                            case '(':
                                openParenCount++;
                                isFunction = true;
                                break;
                            case ')':
                                closingParenCount++;
                                break;
                        }
                        if (openParenCount == closingParenCount)
                        {
                            foundClosingParen = true;
                            break;
                        }
                    }

                    // move the currentPosition to the closing paren
                    currentPosition += closingParenPosition;

                    if (!foundClosingParen)
                    {
                        if (isFunction)
                        {
                            throw new Exception("Undefined Preprocessor Function: " + remainder);
                        }
                        else
                        {
                            throw new Exception("Undefined Preprocessor Variable: " + remainder);
                        }
                    }

                    string subString = remainder.Substring(1, closingParenPosition - 1);
                    string result = null;
                    if (isFunction)
                    {
                        result = this.EvaluateFunction( subString);
                    }
                    else
                    {
                        result = this.GetVariableValue( subString, false);
                    }

                    if (null == result)
                    {
                        if (isFunction)
                        {
                            throw new Exception("Undefined Preprocessor Function: " + subString);
                        }
                        else
                        {
                            throw new Exception("Undefined Preprocessor Variable: " + subString);
                        }
                    }
                    sb.Append(result);
                    end += closingParenPosition + 1;
                }
                else   // just a floating "$" so put it in the final string (i.e. leave it alone) and keep processing
                {
                    sb.Append('$');
                }
            }

            if (end < value.Length)
            {
                sb.Append(value.Substring(end));
            }

            return sb.ToString();
        }

        public void PreprocessPragma(string pragmaName, string args, XmlWriter writer)
        {
            string[] prefixParts = pragmaName.Split(variableSplitter, 2);
            // Check to make sure there are 2 parts and neither is an empty string.
            if (2 != prefixParts.Length)
            {
                Console.WriteLine("[Warning] Preprocessor Unknown Pragma: " + pragmaName);
            }
            string prefix = prefixParts[0];
            string pragma = prefixParts[1];

            if (String.IsNullOrEmpty(prefix) || String.IsNullOrEmpty(pragma))
            {
                Console.WriteLine("[Warning] Preprocessor Unknown Pragma: " + pragmaName);
            }

            switch (prefix)
            {
                default:
                    PreprocessorExtension extension = (PreprocessorExtension)this.extensionsByPrefix[prefix];
                    if (null == extension || !extension.ProcessPragma( prefix, pragma, args, writer))
                    {
                        Console.WriteLine("[Warning] Preprocessor Unknown Pragma: " + pragmaName);
                    }
                    break;
            }
        }

        public string EvaluateFunction(string function)
        {
            string[] prefixParts = function.Split(variableSplitter, 2);
            // Check to make sure there are 2 parts and neither is an empty string.
            if (2 != prefixParts.Length || 0 >= prefixParts[0].Length || 0 >= prefixParts[1].Length)
            {
                throw new Exception("Invalid Preprocessor Function: " + function);
            }
            string prefix = prefixParts[0];

            string[] functionParts = prefixParts[1].Split(new char[] { '(' }, 2);
            // Check to make sure there are 2 parts, neither is an empty string, and the second part ends with a closing paren.
            if (2 != functionParts.Length || 0 >= functionParts[0].Length || 0 >= functionParts[1].Length || !functionParts[1].EndsWith(")", StringComparison.Ordinal))
            {
                throw new Exception("Invalid Preprocessor Function: " + function);
            }
            string functionName = functionParts[0];

            // Remove the trailing closing paren.
            string allArgs = functionParts[1].Substring(0, functionParts[1].Length - 1);

            // Parse the arguments and preprocess them.
            string[] args = allArgs.Split(argumentSplitter);
            for (int i = 0; i < args.Length; i++)
            {
                args[i] = this.PreprocessString( args[i].Trim());
            }

            string result = this.EvaluateFunction( prefix, functionName, args);

            // If the function didn't evaluate, try to evaluate the original value as a variable to support 
            // the use of open and closed parens inside variable names. Example: $(env.ProgramFiles(x86)) should resolve.
            if (null == result)
            {
                result = this.GetVariableValue( function, false);
            }

            return result;
        }

        public string EvaluateFunction(string prefix, string function, string[] args)
        {
            if (String.IsNullOrEmpty(prefix))
            {
                throw new ArgumentNullException("prefix");
            }

            if (String.IsNullOrEmpty(function))
            {
                throw new ArgumentNullException("function");
            }

            switch (prefix)
            {
                case "fun":
                    switch (function)
                    {
                        case "AutoVersion":
                            // Make sure the base version is specified
                            if (args.Length == 0 || args[0].Length == 0)
                            {
                                throw new Exception("Invalid Preprocessor Function Auto Version.");
                            }

                            // Build = days since 1/1/2000; Revision = seconds since midnight / 2
                            DateTime now = DateTime.Now.ToUniversalTime();
                            TimeSpan tsBuild = now - new DateTime(2000, 1, 1);
                            TimeSpan tsRevision = now - new DateTime(now.Year, now.Month, now.Day);

                            return String.Format("{0}.{1}.{2}", args[0], (int)tsBuild.TotalDays, (int)(tsRevision.TotalSeconds / 2));

                        // Add any core defined functions here
                        default:
                            return null;
                    }
                default:
                    PreprocessorExtension extension = (PreprocessorExtension)this.extensionsByPrefix[prefix];
                    if (null != extension)
                    {
                        try
                        {
                            return extension.EvaluateFunction(prefix, function, args);
                        }
                        catch (Exception e)
                        {
                            throw new Exception(e.Message);
                        }
                    }
                    else
                    {
                        return null;
                    }
            }
        }

        public string GetVariableValue(string variable, bool allowMissingPrefix)
        {
            // Strip the "$(" off the front.
            if (variable.StartsWith("$(", StringComparison.Ordinal))
            {
                variable = variable.Substring(2);
            }

            string[] parts = variable.Split(variableSplitter, 2);

            if (1 == parts.Length) // missing prefix
            {
                if (allowMissingPrefix)
                {
                    return this.GetVariableValue( "var", parts[0]);
                }
                else
                {
                    throw new Exception("Invalid Preprocessor Variable: " + variable);
                }
            }
            else
            {
                // check for empty variable name
                if (0 < parts[1].Length)
                {
                    string result = this.GetVariableValue( parts[0], parts[1]);

                    // If we didn't find it and we allow missing prefixes and the variable contains a dot, perhaps the dot isn't intended to indicate a prefix
                    if (null == result && allowMissingPrefix && variable.Contains("."))
                    {
                        result = this.GetVariableValue( "var", variable);
                    }

                    return result;
                }
                else
                {
                    throw new Exception("Invalid Preprocessor Variable: " + variable);
                }
            }
        }

        public string GetVariableValue(string prefix, string name)
        {
            if (String.IsNullOrEmpty(prefix))
            {
                throw new ArgumentNullException("prefix");
            }

            if (String.IsNullOrEmpty(name))
            {
                throw new ArgumentNullException("name");
            }

            switch (prefix)
            {
                case "env":
                    return Environment.GetEnvironmentVariable(name);
                case "sys":
                    switch (name)
                    {
                        case "CURRENTDIR":
                            return String.Concat(Directory.GetCurrentDirectory(), Path.DirectorySeparatorChar);
                        case "SOURCEFILEDIR":
                            return String.Concat(Path.DirectorySeparatorChar);
                        case "SOURCEFILEPATH":
                            return null;
                        case "PLATFORM":
                            Console.WriteLine("[Warning] Deprecated PreProc Variable: $(sys.PLATFORM), $(sys.BUILDARCH)");
                            goto case "BUILDARCH";

                        case "BUILDARCH":
                            switch (this.currentPlatform)
                            {
                                case Platform.X86:
                                    return "x86";
                                case Platform.X64:
                                    return "x64";
                                case Platform.IA64:
                                    return "ia64";
                                case Platform.ARM:
                                    return "arm";
                                default:
                                    throw new ArgumentException(this.currentPlatform.ToString());
                            }
                        default:
                            return null;
                    }
                case "var":
                    return (string)this.variables[name];
                default:
                    PreprocessorExtension extension = (PreprocessorExtension)this.extensionsByPrefix[prefix];
                    if (null != extension)
                    {
                        try
                        {
                            return extension.GetVariableValue(prefix, name);
                        }
                        catch (Exception e)
                        {
                            throw new Exception(e.Message);
                        }
                    }
                    else
                    {
                        return null;
                    }
            }
        }

        internal void AddVariable(string name, string value)
        {
            AddVariable( name, value, true);
        }

        internal void AddVariable(string name, string value, bool showWarning)
        {
            string currentValue = this.GetVariableValue( "var", name);

            if (null == currentValue)
            {
                this.variables.Add(name, value);
            }
            else
            {
                if (showWarning)
                {
                    Console.WriteLine("[Warning] Variable Declaration Collision: name-" + name + " value-" + value + " currentvalue-" + currentValue);
                }

                this.variables[name] = value;
            }
        }

        internal void RemoveVariable(string name)
        {
            if (this.variables.Contains(name))
            {
                this.variables.Remove(name);
            }
            else
            {
                throw new Exception("Cannot Reundefine Variable: " + name);
            }
        }
    }
}
