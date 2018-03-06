/* 
    Preprocessor implementation is from https://github.com/wixtoolset 
*/

namespace XMLPreprocessor
{
    public enum IfState
    {
        Unknown,
        If,
        ElseIf,
        Else,
    }

    public sealed class IfContext
    {
        private bool active;
        private bool keep;
        private bool everKept;
        private IfState state;

        public IfContext()
        {
            this.active = false;
            this.keep = false;
            this.everKept = true;
            this.state = IfState.If;
        }

        public IfContext(bool active, bool keep, IfState state)
        {
            this.active = active;
            this.keep = keep;
            this.everKept = keep;
            this.state = state;
        }

        public bool Active
        {
            get { return this.active; }
            set { this.active = value; }
        }

        public bool IsTrue
        {
            get
            {
                return this.keep;
            }

            set
            {
                this.keep = value;
                if (this.keep)
                {
                    this.everKept = true;
                }
            }
        }

        public bool WasEverTrue
        {
            get { return this.everKept; }
        }

        public IfState IfState
        {
            get { return this.state; }
            set { this.state = value; }
        }
    }
}
