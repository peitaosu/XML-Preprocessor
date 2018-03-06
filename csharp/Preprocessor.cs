/* 
    Preprocessor implementation is from https://github.com/wixtoolset 
*/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace XMLPreprocessor
{

    public sealed class Preprocessor
    {
        private static readonly Regex defineRegex = new Regex(@"^\s*(?<varName>.+?)\s*(=\s*(?<varValue>.+?)\s*)?$", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.ExplicitCapture);
        private static readonly Regex pragmaRegex = new Regex(@"^\s*(?<pragmaName>.+?)(?<pragmaValue>[\s\(].+?)?$", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.ExplicitCapture);

        private StringCollection includeSearchPaths;

        private Stack sourceStack;

        private PreprocessorCore core;
        private TextWriter preprocessOut;

        private Stack includeNextStack;
        private Stack currentFileStack;

        private Platform currentPlatform;

        public Preprocessor()
        {
            this.includeSearchPaths = new StringCollection();

            this.sourceStack = new Stack();

            this.includeNextStack = new Stack();
            this.currentFileStack = new Stack();

            this.currentPlatform = Platform.X86;
        }

        public event IfDefEventHandler IfDef;

        public event IncludedFileEventHandler IncludedFile;

        public event ProcessedStreamEventHandler ProcessedStream;

        private enum PreprocessorOperation
        {
            And,
            Or,
            Not
        }

        public Platform CurrentPlatform
        {
            get { return this.currentPlatform; }
            set { this.currentPlatform = value; }
        }

        public StringCollection IncludeSearchPaths
        {
            get { return this.includeSearchPaths; }
        }

        public TextWriter PreprocessOut
        {
            get { return this.preprocessOut; }
            set { this.preprocessOut = value; }
        }

        [SuppressMessage("Microsoft.Design", "CA1059:MembersShouldNotExposeCertainConcreteTypes")]
        public XmlDocument Process(string sourceFile, Hashtable variables)
        {
            Stream processed = new MemoryStream();
            XmlDocument sourceDocument = new XmlDocument();

            try
            {
                this.core = new PreprocessorCore(sourceFile, variables);
                this.core.CurrentPlatform = this.currentPlatform;
                this.currentFileStack.Clear();
                this.currentFileStack.Push(this.core.GetVariableValue("sys", "SOURCEFILEDIR"));

                // open the source file for processing
                using (Stream sourceStream = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                {


                    XmlReader reader = new XmlTextReader(sourceStream);
                    XmlTextWriter writer = new XmlTextWriter(processed, Encoding.UTF8);

                    // process the reader into the writer
                    try
                    {
                        this.PreprocessReader(false, reader, writer, 0);
                    }
                    catch (XmlException e)
                    {
                        throw new Exception(e.Message);
                    }

                    writer.Flush();
                }

                // fire event with processed stream
                ProcessedStreamEventArgs args = new ProcessedStreamEventArgs(sourceFile, processed);
                this.OnProcessedStream(args);

                // create an XML Document from the post-processed memory stream
                XmlTextReader xmlReader = null;

                try
                {
                    // create an XmlReader with the path to the original file
                    // to properly set the BaseURI property of the XmlDocument
                    processed.Position = 0;
                    xmlReader = new XmlTextReader(new Uri(Path.GetFullPath(sourceFile)).AbsoluteUri, processed);
                    sourceDocument.Load(xmlReader);

                }
                catch (XmlException e)
                {
                    throw new Exception(e.Message);
                }
                finally
                {
                    if (null != xmlReader)
                    {
                        xmlReader.Close();
                    }
                }

            }
            finally
            {
                ;
            }

            if (this.core.EncounteredError)
            {
                return null;
            }
            else
            {
                if (null != this.preprocessOut)
                {
                    sourceDocument.Save(this.preprocessOut);
                    this.preprocessOut.Flush();
                }

                return sourceDocument;
            }
        }

        private static bool IsOperator(string operation)
        {
            if (operation == null)
            {
                return false;
            }

            operation = operation.Trim();
            if (0 == operation.Length)
            {
                return false;
            }

            if ("=" == operation ||
                "!=" == operation ||
                "<" == operation ||
                "<=" == operation ||
                ">" == operation ||
                ">=" == operation ||
                "~=" == operation)
            {
                return true;
            }
            return false;
        }

        private static bool InsideQuotes(string expression, int index)
        {
            if (index == -1)
            {
                return false;
            }

            int numQuotes = 0;
            int tmpIndex = 0;
            while (-1 != (tmpIndex = expression.IndexOf('\"', tmpIndex, index - tmpIndex)))
            {
                numQuotes++;
                tmpIndex++;
            }

            // found an even number of quotes before the index, so we're not inside
            if (numQuotes % 2 == 0)
            {
                return false;
            }

            // found an odd number of quotes, so we are inside
            return true;
        }

        private void OnIfDef(IfDefEventArgs ea)
        {
            if (null != this.IfDef)
            {
                this.IfDef(this, ea);
            }
        }

        private void OnIncludedFile(IncludedFileEventArgs ea)
        {
            if (null != this.IncludedFile)
            {
                this.IncludedFile(this, ea);
            }
        }

        private void OnProcessedStream(ProcessedStreamEventArgs ea)
        {
            if (null != this.ProcessedStream)
            {
                this.ProcessedStream(this, ea);
            }
        }

        private static bool StartsWithKeyword(string expression, PreprocessorOperation operation)
        {
            expression = expression.ToUpper(CultureInfo.InvariantCulture);
            switch (operation)
            {
                case PreprocessorOperation.Not:
                    if (expression.StartsWith("NOT ", StringComparison.Ordinal) || expression.StartsWith("NOT(", StringComparison.Ordinal))
                    {
                        return true;
                    }
                    break;
                case PreprocessorOperation.And:
                    if (expression.StartsWith("AND ", StringComparison.Ordinal) || expression.StartsWith("AND(", StringComparison.Ordinal))
                    {
                        return true;
                    }
                    break;
                case PreprocessorOperation.Or:
                    if (expression.StartsWith("OR ", StringComparison.Ordinal) || expression.StartsWith("OR(", StringComparison.Ordinal))
                    {
                        return true;
                    }
                    break;
                default:
                    break;
            }
            return false;
        }

        private void PreprocessReader(bool include, XmlReader reader, XmlWriter writer, int offset)
        {
            Stack stack = new Stack(5);
            IfContext context = new IfContext(true, true, IfState.Unknown); // start by assuming we want to keep the nodes in the source code

            // process the reader into the writer
            while (reader.Read())
            {

                // check for changes in conditional processing
                if (XmlNodeType.ProcessingInstruction == reader.NodeType)
                {
                    bool ignore = false;
                    string name = null;

                    switch (reader.LocalName)
                    {
                        case "if":
                            stack.Push(context);
                            if (context.IsTrue)
                            {
                                context = new IfContext(context.IsTrue & context.Active, this.EvaluateExpression(reader.Value), IfState.If);
                            }
                            else // Use a default IfContext object so we don't try to evaluate the expression if the context isn't true
                            {
                                context = new IfContext();
                            }
                            ignore = true;
                            break;
                        case "ifdef":
                            stack.Push(context);
                            name = reader.Value.Trim();
                            if (context.IsTrue)
                            {
                                context = new IfContext(context.IsTrue & context.Active, (null != this.core.GetVariableValue(name, true)), IfState.If);
                            }
                            else // Use a default IfContext object so we don't try to evaluate the expression if the context isn't true
                            {
                                context = new IfContext();
                            }
                            ignore = true;
                            OnIfDef(new IfDefEventArgs(true, context.IsTrue, name));
                            break;
                        case "ifndef":
                            stack.Push(context);
                            name = reader.Value.Trim();
                            if (context.IsTrue)
                            {
                                context = new IfContext(context.IsTrue & context.Active, (null == this.core.GetVariableValue(name, true)), IfState.If);
                            }
                            else // Use a default IfContext object so we don't try to evaluate the expression if the context isn't true
                            {
                                context = new IfContext();
                            }
                            ignore = true;
                            OnIfDef(new IfDefEventArgs(false, !context.IsTrue, name));
                            break;
                        case "elseif":
                            if (0 == stack.Count)
                            {
                                throw new Exception("Unmatched Preprocessor Instruction: if, elseif");
                            }

                            if (IfState.If != context.IfState && IfState.ElseIf != context.IfState)
                            {
                                throw new Exception("Unmatched Preprocessor Instruction: if, elseif");
                            }

                            context.IfState = IfState.ElseIf;   // we're now in an elseif
                            if (!context.WasEverTrue)   // if we've never evaluated the if context to true, then we can try this test
                            {
                                context.IsTrue = this.EvaluateExpression(reader.Value);
                            }
                            else if (context.IsTrue)
                            {
                                context.IsTrue = false;
                            }
                            ignore = true;
                            break;
                        case "else":
                            if (0 == stack.Count)
                            {
                                throw new Exception("Unmatched Preprocessor Instruction: if, else");
                            }

                            if (IfState.If != context.IfState && IfState.ElseIf != context.IfState)
                            {
                                throw new Exception("Unmatched Preprocessor Instruction: if, else");
                            }

                            context.IfState = IfState.Else;   // we're now in an else
                            context.IsTrue = !context.WasEverTrue;   // if we were never true, we can be true now
                            ignore = true;
                            break;
                        case "endif":
                            if (0 == stack.Count)
                            {
                                throw new Exception("Unmatched Preprocessor Instruction: if, endif");
                            }

                            context = (IfContext)stack.Pop();
                            ignore = true;
                            break;
                    }

                    if (ignore)   // ignore this node since we just handled it above
                    {
                        continue;
                    }
                }

                if (!context.Active || !context.IsTrue)   // if our context is not true then skip the rest of the processing and just read the next thing
                {
                    continue;
                }

                switch (reader.NodeType)
                {
                    case XmlNodeType.ProcessingInstruction:
                        switch (reader.LocalName)
                        {
                            case "define":
                                this.PreprocessDefine(reader.Value);
                                break;
                            case "error":
                                this.PreprocessError(reader.Value);
                                break;
                            case "warning":
                                this.PreprocessWarning(reader.Value);
                                break;
                            case "undef":
                                this.PreprocessUndef(reader.Value);
                                break;
                            case "include":
                                this.PreprocessInclude(reader.Value, writer);
                                break;
                            case "foreach":
                                this.PreprocessForeach(reader, writer, offset);
                                break;
                            case "endforeach": // endforeach is handled in PreprocessForeach, so seeing it here is an error
                                throw new Exception("Unmatched Preprocessor Instruction: foreach, endforeach");
                            case "pragma":
                                this.PreprocessPragma(reader.Value, writer);
                                break;
                            default:
                                // unknown processing instructions are currently ignored
                                break;
                        }
                        break;
                    case XmlNodeType.Element:
                        bool empty = reader.IsEmptyElement;

                        if (0 < this.includeNextStack.Count && (bool)this.includeNextStack.Peek())
                        {
                            if ("Include" != reader.LocalName)
                            {
                                throw new Exception("Invalid Document Element: include, Include");
                            }

                            this.includeNextStack.Pop();
                            this.includeNextStack.Push(false);
                            break;
                        }

                        // output any necessary preprocessor processing instructions then write the start of the element
                        writer.WriteStartElement(reader.Name);

                        while (reader.MoveToNextAttribute())
                        {
                            string value = this.core.PreprocessString(reader.Value);
                            writer.WriteAttributeString(reader.Name, value);
                        }

                        if (empty)
                        {
                            writer.WriteEndElement();
                        }
                        break;
                    case XmlNodeType.EndElement:
                        if (0 < reader.Depth || !include)
                        {
                            writer.WriteEndElement();
                        }
                        break;
                    case XmlNodeType.Text:
                        string postprocessedText = this.core.PreprocessString(reader.Value);
                        writer.WriteString(postprocessedText);
                        break;
                    case XmlNodeType.CDATA:
                        string postprocessedValue = this.core.PreprocessString(reader.Value);
                        writer.WriteCData(postprocessedValue);
                        break;
                    default:
                        break;
                }
            }

            if (0 != stack.Count)
            {
                throw new Exception("Nonterminated Preprocessor Instruction: if, endif");
            }
        }

        private void PreprocessError(string errorMessage)
        {

            // resolve other variables in the error message
            errorMessage = this.core.PreprocessString(errorMessage);
            throw new Exception("Preprocessor Error: " + errorMessage);
        }

        private void PreprocessWarning(string warningMessage)
        {

            // resolve other variables in the warning message
            warningMessage = this.core.PreprocessString(warningMessage);
            Console.WriteLine("[Warning] Preprocessor Warning: " + warningMessage);
        }

        private void PreprocessDefine(string originalDefine)
        {
            Match match = defineRegex.Match(originalDefine);

            if (!match.Success)
            {
                throw new Exception("Illegal Define Statement: " + originalDefine);
            }

            string defineName = match.Groups["varName"].Value;
            string defineValue = match.Groups["varValue"].Value;

            // strip off the optional quotes
            if (1 < defineValue.Length &&
                   ((defineValue.StartsWith("\"", StringComparison.Ordinal) && defineValue.EndsWith("\"", StringComparison.Ordinal))
                || (defineValue.StartsWith("'", StringComparison.Ordinal) && defineValue.EndsWith("'", StringComparison.Ordinal))))
            {
                defineValue = defineValue.Substring(1, defineValue.Length - 2);
            }

            // resolve other variables in the variable value
            defineValue = this.core.PreprocessString(defineValue);

            if (defineName.StartsWith("var.", StringComparison.Ordinal))
            {
                this.core.AddVariable(defineName.Substring(4), defineValue);
            }
            else
            {
                this.core.AddVariable(defineName, defineValue);
            }
        }

        private void PreprocessUndef(string originalDefine)
        {
            
            string name = this.core.PreprocessString(originalDefine.Trim());

            if (name.StartsWith("var.", StringComparison.Ordinal))
            {
                this.core.RemoveVariable(name.Substring(4));
            }
            else
            {
                this.core.RemoveVariable(name);
            }
        }

        private void PreprocessInclude(string includePath, XmlWriter writer)
        {
            

            // preprocess variables in the path
            includePath = this.core.PreprocessString(includePath);

            FileInfo includeFile = this.GetIncludeFile(includePath);

            if (null == includeFile)
            {
                throw new FileNotFoundException(includePath);
            }

            using (Stream includeStream = new FileStream(includeFile.FullName, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                XmlReader reader = new XmlTextReader(includeStream);

                this.PushInclude(includeFile.FullName);

                // process the included reader into the writer
                try
                {
                    this.PreprocessReader(true, reader, writer, 0);
                }
                catch (XmlException e)
                {
                    throw new Exception(e.Message);
                }

                this.OnIncludedFile(new IncludedFileEventArgs(includeFile.FullName));

                this.PopInclude();
            }
        }

        private void PreprocessForeach(XmlReader reader, XmlWriter writer, int offset)
        {
            // find the "in" token
            int indexOfInToken = reader.Value.IndexOf(" in ", StringComparison.Ordinal);
            if (0 > indexOfInToken)
            {
                throw new Exception("Illegal Foreach: " + reader.Value);
            }

            // parse out the variable name
            string varName = reader.Value.Substring(0, indexOfInToken).Trim();
            string varValuesString = reader.Value.Substring(indexOfInToken + 4).Trim();

            // preprocess the variable values string because it might be a variable itself
            varValuesString = this.core.PreprocessString(varValuesString);

            string[] varValues = varValuesString.Split(';');

            // go through all the empty strings
            while (reader.Read() && XmlNodeType.Whitespace == reader.NodeType)
            {
            }

            // get the offset of this xml fragment (for some reason its always off by 1)
            IXmlLineInfo lineInfoReader = reader as IXmlLineInfo;
            if (null != lineInfoReader)
            {
                offset += lineInfoReader.LineNumber - 1;
            }

            XmlTextReader textReader = reader as XmlTextReader;
            // dump the xml to a string (maintaining whitespace if possible)
            if (null != textReader)
            {
                textReader.WhitespaceHandling = WhitespaceHandling.All;
            }

            string fragment = null;
            StringBuilder fragmentBuilder = new StringBuilder();
            int nestedForeachCount = 1;
            while (nestedForeachCount != 0)
            {
                if (reader.NodeType == XmlNodeType.ProcessingInstruction)
                {
                    switch (reader.LocalName)
                    {
                        case "foreach":
                            nestedForeachCount++;
                            // Output the foreach statement
                            fragmentBuilder.AppendFormat("<?foreach {0}?>", reader.Value);
                            break;
                        case "endforeach":
                            nestedForeachCount--;
                            if (0 != nestedForeachCount)
                            {
                                fragmentBuilder.Append("<?endforeach ?>");
                            }
                            break;
                        default:
                            fragmentBuilder.AppendFormat("<?{0} {1}?>", reader.LocalName, reader.Value);
                            break;
                    }
                }
                else if (reader.NodeType == XmlNodeType.Element)
                {
                    fragmentBuilder.Append(reader.ReadOuterXml());
                    continue;
                }
                else if (reader.NodeType == XmlNodeType.Whitespace)
                {
                    // Or output the whitespace
                    fragmentBuilder.Append(reader.Value);
                }
                else if (reader.NodeType == XmlNodeType.None)
                {
                    throw new Exception("Expected End Foreach.");
                }

                reader.Read();
            }

            fragment = fragmentBuilder.ToString();

            // process each iteration, updating the variable's value each time
            foreach (string varValue in varValues)
            {
                // Always overwrite foreach variables.
                this.core.AddVariable(varName, varValue, false);

                XmlTextReader loopReader;
                loopReader = new XmlTextReader(fragment, XmlNodeType.Element, new XmlParserContext(null, null, null, XmlSpace.Default));

                try
                {
                    this.PreprocessReader(false, loopReader, writer, offset);
                }
                catch (XmlException e)
                {
                    throw new Exception(e.Message);
                }
            }
        }

        private void PreprocessPragma(string pragmaText, XmlWriter writer)
        {
            Match match = pragmaRegex.Match(pragmaText);
            

            if (!match.Success)
            {
                throw new Exception("Invalid Preprocessor Pragma: " + pragmaText);
            }

            // resolve other variables in the pragma argument(s)
            string pragmaArgs = this.core.PreprocessString(match.Groups["pragmaValue"].Value).Trim();

            try
            {
                this.core.PreprocessPragma(match.Groups["pragmaName"].Value.Trim(), pragmaArgs, writer);
            }
            catch (Exception e)
            {
                throw new Exception(e.Message);
            }
        }

        private string GetNextToken(string originalExpression, ref string expression, out bool stringLiteral)
        {
            stringLiteral = false;
            string token = String.Empty;
            expression = expression.Trim();
            if (0 == expression.Length)
            {
                return String.Empty;
            }

            if (expression.StartsWith("\"", StringComparison.Ordinal))
            {
                stringLiteral = true;
                int endingQuotes = expression.IndexOf('\"', 1);
                if (-1 == endingQuotes)
                {
                    throw new Exception("Unmatched Quotes In Expression: " + originalExpression);
                }

                // cut the quotes off the string
                token = this.core.PreprocessString(expression.Substring(1, endingQuotes - 1));  

                // advance past this string
                expression = expression.Substring(endingQuotes + 1).Trim();
            }
            else if (expression.StartsWith("$(", StringComparison.Ordinal))
            {
                // Find the ending paren of the expression
                int endingParen = -1;
                int openedCount = 1;
                for (int i = 2; i < expression.Length; i++ )
                {
                    if ('(' == expression[i])
                    {
                        openedCount++;
                    }
                    else if (')' == expression[i])
                    {
                        openedCount--;
                    }

                    if (openedCount == 0)
                    {
                        endingParen = i;
                        break;
                    }
                }

                if (-1 == endingParen)
                {
                    throw new Exception("Unmatched Parenthesis In Expression: " + originalExpression);
                }
                token = expression.Substring(0, endingParen + 1);  

                // Advance past this variable
                expression = expression.Substring(endingParen + 1).Trim();
            }
            else 
            {
                // Cut the token off at the next equal, space, inequality operator,
                // or end of string, whichever comes first
                int space             = expression.IndexOf(" ", StringComparison.Ordinal);
                int equals            = expression.IndexOf("=", StringComparison.Ordinal);
                int lessThan          = expression.IndexOf("<", StringComparison.Ordinal);
                int lessThanEquals    = expression.IndexOf("<=", StringComparison.Ordinal);
                int greaterThan       = expression.IndexOf(">", StringComparison.Ordinal);
                int greaterThanEquals = expression.IndexOf(">=", StringComparison.Ordinal);
                int notEquals         = expression.IndexOf("!=", StringComparison.Ordinal);
                int equalsNoCase      = expression.IndexOf("~=", StringComparison.Ordinal);
                int closingIndex;

                if (space == -1)
                {
                    space = Int32.MaxValue;
                }

                if (equals == -1)
                {
                    equals = Int32.MaxValue;
                }

                if (lessThan == -1)
                {
                    lessThan = Int32.MaxValue;
                }

                if (lessThanEquals == -1)
                {
                    lessThanEquals = Int32.MaxValue;
                }

                if (greaterThan == -1)
                {
                    greaterThan = Int32.MaxValue;
                }

                if (greaterThanEquals == -1)
                {
                    greaterThanEquals = Int32.MaxValue;
                }

                if (notEquals == -1)
                {
                    notEquals = Int32.MaxValue;
                }

                if (equalsNoCase == -1)
                {
                    equalsNoCase = Int32.MaxValue;
                }

                closingIndex = Math.Min(space, Math.Min(equals, Math.Min(lessThan, Math.Min(lessThanEquals, Math.Min(greaterThan, Math.Min(greaterThanEquals, Math.Min(equalsNoCase, notEquals)))))));

                if (Int32.MaxValue == closingIndex)
                {
                    closingIndex = expression.Length;
                }

                // If the index is 0, we hit an operator, so return it
                if (0 == closingIndex)
                {
                    // Length 2 operators
                    if (closingIndex == lessThanEquals || closingIndex == greaterThanEquals || closingIndex == notEquals || closingIndex == equalsNoCase)
                    {
                        closingIndex = 2;
                    }
                    else // Length 1 operators
                    {
                        closingIndex = 1;
                    }
                }

                // Cut out the new token
                token = expression.Substring(0, closingIndex).Trim();
                expression = expression.Substring(closingIndex).Trim();
            }

            return token;
        }

        private string EvaluateVariable(string originalExpression, string variable)
        {
            // By default it's a literal and will only be evaluated if it
            // matches the variable format
            string varValue = variable;

            if (variable.StartsWith("$(", StringComparison.Ordinal))
            {
                try
                {
                    varValue = this.core.PreprocessString(variable);
                }
                catch (ArgumentNullException)
                {
                    // non-existent variables are expected
                    varValue = null;
                }
            }
            else if (variable.IndexOf("(", StringComparison.Ordinal) != -1 || variable.IndexOf(")", StringComparison.Ordinal) != -1)
            {
                // make sure it doesn't contain parenthesis
                throw new Exception("Unmatched Parenthesis In Expression: " + originalExpression);
            }
            else if (variable.IndexOf("\"", StringComparison.Ordinal) != -1)
            {
                // shouldn't contain quotes
                throw new Exception("Unmatched Quotes In Expression: " + originalExpression);
            }

            return varValue;
        }

        private void GetNameValuePair(string originalExpression, ref string expression, out string leftValue, out string operation, out string rightValue)
        {
            bool stringLiteral;
            leftValue = this.GetNextToken(originalExpression, ref expression, out stringLiteral);

            // If it wasn't a string literal, evaluate it
            if (!stringLiteral)
            {
                leftValue = this.EvaluateVariable(originalExpression, leftValue);
            }

            // Get the operation
            operation = this.GetNextToken(originalExpression, ref expression, out stringLiteral);
            if (IsOperator(operation))
            {
                if (stringLiteral)
                {
                    throw new Exception("Unmatched Quotes In Expression: " + originalExpression);
                }

                rightValue = this.GetNextToken(originalExpression, ref expression, out stringLiteral);

                // If it wasn't a string literal, evaluate it
                if (!stringLiteral)
                {
                    rightValue = this.EvaluateVariable(originalExpression, rightValue);
                }
            }
            else
            {
                // Prepend the token back on the expression since it wasn't an operator
                // and put the quotes back on the literal if necessary
                
                if (stringLiteral)
                {
                    operation = "\"" + operation + "\"";
                }
                expression = (operation + " " + expression).Trim();

                // If no operator, just check for existence
                operation = "";
                rightValue = "";
            }
        }

        private bool EvaluateAtomicExpression(string originalExpression, ref string expression)
        {
            // Quick test to see if the first token is a variable
            bool startsWithVariable = expression.StartsWith("$(", StringComparison.Ordinal);

            string leftValue;
            string rightValue;
            string operation;
            this.GetNameValuePair(originalExpression, ref expression, out leftValue, out operation, out rightValue);

            bool expressionValue = false;

            // If the variables don't exist, they were evaluated to null
            if (null == leftValue || null == rightValue)
            {
                if (operation.Length > 0)
                {
                    throw new Exception("Expected Variable: " + originalExpression);
                }

                // false expression
            }
            else if (operation.Length == 0)
            {
                // There is no right side of the equation.
                // If the variable was evaluated, it exists, so the expression is true
                if (startsWithVariable)
                {
                    expressionValue = true;
                }
                else
                {
                    throw new Exception("Unexpected Literal: " + originalExpression);
                }
            }
            else
            {
                leftValue = leftValue.Trim();
                rightValue = rightValue.Trim();
                if ("=" == operation)
                {
                    if (leftValue == rightValue)
                    {
                        expressionValue = true;
                    }
                }
                else if ("!=" == operation)
                {
                    if (leftValue != rightValue)
                    {
                        expressionValue = true;
                    }
                }
                else if ("~=" == operation)
                {
                    if (String.Equals(leftValue, rightValue, StringComparison.OrdinalIgnoreCase))
                    {
                        expressionValue = true;
                    }
                }
                else 
                {
                    // Convert the numbers from strings
                    int rightInt;
                    int leftInt;
                    try
                    {
                        rightInt = Int32.Parse(rightValue, CultureInfo.InvariantCulture);
                        leftInt = Int32.Parse(leftValue, CultureInfo.InvariantCulture);
                    }
                    catch (FormatException)
                    {
                        throw new Exception("Illegal Integer In Expression: " + originalExpression);
                    }
                    catch (OverflowException)
                    {
                        throw new Exception("Illegal Integer In Expression: " + originalExpression);
                    }

                    // Compare the numbers
                    if ("<" == operation && leftInt < rightInt ||
                        "<=" == operation && leftInt <= rightInt ||
                        ">" == operation && leftInt > rightInt ||
                        ">=" == operation && leftInt >= rightInt)
                    {
                        expressionValue = true;
                    }
                }
            }

            return expressionValue;
        }

        private string GetParenthesisExpression(string originalExpression, string expression, out int endSubExpression)
        {
            endSubExpression = 0;

            // if the expression doesn't start with parenthesis, leave it alone
            if (!expression.StartsWith("(", StringComparison.Ordinal))
            {
                return expression;
            }

            // search for the end of the expression with the matching paren
            int openParenIndex = 0;
            int closeParenIndex = 1;
            while (openParenIndex != -1 && openParenIndex < closeParenIndex)
            {
                closeParenIndex = expression.IndexOf(')', closeParenIndex);
                if (closeParenIndex == -1)
                {
                    throw new Exception("Unmatched Parenthesis In Expression: " + originalExpression);
                }

                if (InsideQuotes(expression, closeParenIndex))
                {
                    // ignore stuff inside quotes (it's a string literal)
                }
                else
                {
                    // Look to see if there is another open paren before the close paren
                    // and skip over the open parens while they are in a string literal
                    do
                    {
                        openParenIndex++;
                        openParenIndex = expression.IndexOf('(', openParenIndex, closeParenIndex - openParenIndex);
                    }
                    while (InsideQuotes(expression, openParenIndex));
                }

                // Advance past the closing paren
                closeParenIndex++;
            }

            endSubExpression = closeParenIndex;

            // Return the expression minus the parenthesis
            return expression.Substring(1, closeParenIndex - 2);
        }

        private void UpdateExpressionValue(ref bool currentValue, PreprocessorOperation operation, bool prevResult)
        {
            switch (operation)
            {
                case PreprocessorOperation.And:
                    currentValue = currentValue && prevResult;
                    break;
                case PreprocessorOperation.Or:
                    currentValue = currentValue || prevResult;
                    break;
                case PreprocessorOperation.Not:
                    currentValue = !currentValue;
                    break;
                default:
                    throw new Exception("Unexpected Preprocessor Operator: " + operation.ToString());
            }
        }

        private bool EvaluateExpression(string expression)
        {
            string tmpExpression = expression;
            return this.EvaluateExpressionRecurse(expression, ref tmpExpression, PreprocessorOperation.And, true);
        }

        private bool EvaluateExpressionRecurse(string originalExpression,  ref string expression,  PreprocessorOperation prevResultOperation,  bool prevResult)
        {
            bool expressionValue = false;
            expression = expression.Trim();
            if (expression.Length == 0)
            {
                throw new Exception("Unexpected Empty Subexpression: " + originalExpression);
            }

            // If the expression starts with parenthesis, evaluate it
            if (expression.IndexOf('(') == 0)
            {
                int endSubExpressionIndex;
                string subExpression = this.GetParenthesisExpression(originalExpression, expression, out endSubExpressionIndex);
                expressionValue = this.EvaluateExpressionRecurse(originalExpression, ref subExpression, PreprocessorOperation.And, true);

                // Now get the rest of the expression that hasn't been evaluated
                expression = expression.Substring(endSubExpressionIndex).Trim();
            }
            else
            {
                // Check for NOT
                if (StartsWithKeyword(expression, PreprocessorOperation.Not))
                {
                    expression = expression.Substring(3).Trim();
                    if (expression.Length == 0)
                    {
                        throw new Exception("Expected Expression After Not: " + originalExpression);
                    }

                    expressionValue = this.EvaluateExpressionRecurse(originalExpression, ref expression, PreprocessorOperation.Not, true);
                }
                else // Expect a literal
                {
                    expressionValue = this.EvaluateAtomicExpression(originalExpression, ref expression);

                    // Expect the literal that was just evaluated to already be cut off
                }
            }
            this.UpdateExpressionValue(ref expressionValue, prevResultOperation, prevResult);

            // If there's still an expression left, it must start with AND or OR.
            if (expression.Trim().Length > 0)
            {
                if (StartsWithKeyword(expression, PreprocessorOperation.And))
                {
                    expression = expression.Substring(3);
                    return this.EvaluateExpressionRecurse(originalExpression, ref expression, PreprocessorOperation.And, expressionValue);
                }
                else if (StartsWithKeyword(expression, PreprocessorOperation.Or))
                {
                    expression = expression.Substring(2);
                    return this.EvaluateExpressionRecurse(originalExpression, ref expression, PreprocessorOperation.Or, expressionValue);
                }
                else
                {
                    throw new Exception("Invalid Sub Expression: " + expression);
                }
            }

            return expressionValue;
        }

        private void PushInclude(string fileName)
        {
            if (1023 < this.currentFileStack.Count)
            {
                throw new Exception("Too Deeply Included: " + this.currentFileStack.Count);
            }

            this.currentFileStack.Push(fileName);
            this.includeNextStack.Push(true);
        }

        private void PopInclude()
        {

            this.currentFileStack.Pop();
            this.includeNextStack.Pop();
        }

        private FileInfo GetIncludeFile(string includePath)
        {
            string finalIncludePath = null;
            FileInfo includeFile = null;

            includePath = includePath.Trim();

            // remove quotes (only if they match)
            if ((includePath.StartsWith("\"", StringComparison.Ordinal) && includePath.EndsWith("\"", StringComparison.Ordinal)) ||
                (includePath.StartsWith("'", StringComparison.Ordinal) && includePath.EndsWith("'", StringComparison.Ordinal)))
            {
                includePath = includePath.Substring(1, includePath.Length - 2);
            }

            // check if the include file is a full path
            if (Path.IsPathRooted(includePath))
            {
                if (File.Exists(includePath))
                {
                    finalIncludePath = includePath;
                }
            }
            else // relative path
            {
                // build a string to test the directory containing the source file first
                string includeTestPath = String.Concat(Path.GetDirectoryName((string)this.currentFileStack.Peek()), Path.DirectorySeparatorChar, includePath);

                // test the source file directory
                if (File.Exists(includeTestPath))
                {
                    finalIncludePath = includeTestPath;
                }
                else // test all search paths in the order specified on the command line
                {
                    foreach (string includeSearchPath in this.includeSearchPaths)
                    {
                        StringBuilder pathBuilder = new StringBuilder(includeSearchPath);

                        // put the slash at the end of the path if its missing
                        if (!includeSearchPath.EndsWith("/", StringComparison.Ordinal) && !includeSearchPath.EndsWith("\\", StringComparison.Ordinal))
                        {
                            pathBuilder.Append('\\');
                        }

                        // append the relative path to the included file
                        pathBuilder.Append(includePath);

                        includeTestPath = pathBuilder.ToString();

                        // if the path exists, we have found the final string
                        if (File.Exists(includeTestPath))
                        {
                            finalIncludePath = includeTestPath;
                            break;
                        }
                    }
                }
            }

            // create the FileInfo if the path exists
            if (null != finalIncludePath)
            {
                includeFile = new FileInfo(finalIncludePath);
            }

            return includeFile;
        }

    }
}
