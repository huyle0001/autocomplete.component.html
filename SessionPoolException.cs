using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Laserfiche.SessionPool
{
    public class SessionPoolException : Exception
    {
        public int ErrorCode { get; private set; }
        public SessionPoolException(string message, int errorCode) : base(message) 
        {
            this.ErrorCode = errorCode;
        }
    }

    public class MaxRetryException : SessionPoolException
    {
        public MaxRetryException() : base("Failed: Maximum retry limit reached. No sessions avaliable.", 408) { }
    }
}
