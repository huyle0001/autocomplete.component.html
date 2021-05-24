using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Laserfiche.RepositoryAccess;

namespace Laserfiche.SessionPool
{
    /// <summary>
    /// Wrapper for Laserfiche Sessions to provide concurrency checks
    /// </summary>
    public class PooledSession : IDisposable
    {
        public readonly TimeSpan LOCK_TIMEOUT = TimeSpan.FromMilliseconds(500);
        public readonly int RETRY_LIMIT = 5;
        private string _connectionString;
        private Session _masterSession;
        private bool _isInUse = false;
        private object _lockToken = new object();

        public string SessionName                               { get; private set; }
        public int SessionId                                    { get => _masterSession.Id; }
        public SessionPool SessionPool                          { get; private set; }
        public bool IsInUse                                     { get => _isInUse; }
        public bool IsAlive                                     { get => CheckIsMasterSessionAlive(); }

        /// <summary>
        /// Event that fires when the session status changes from Alive to not. This event is bound to the IsAlive property.
        /// It is fired when IsAlive is evaluated after connecting and returns false.
        /// </summary>
        public event EventHandler PooledSessionClosedEvent;

        /// <summary>
        /// Creates a new pooled session. The pooled session must be connected to a repository before
        /// use.
        /// </summary>
        /// <param name="name">Used for tracking. Typically the name of the app and a id number.</param>
        /// <param name="pool">The pool this session is associated with.</param>
        public PooledSession(string name, SessionPool pool) 
        {
            SessionName = name;
            SessionPool = pool;
            _isInUse = false;
            _masterSession = null;
        }

        /// <summary>
        /// Logs the pooled session into the target repository. If username is set to null, connect with the current thread's windows account.
        /// </summary>
        /// <param name="username">Set to Null to use Windows Authentication</param>
        /// <param name="password"></param>
        public void Connect(RepositoryRegistration repo, string username, string password, string applicationkKey = null)
        {
            _Connect(repo, username, password, applicationkKey); 
        }

        /// <summary>
        /// Logs the pooled session into the target repository using the current thread's windows account. 
        /// </summary>
        /// <param name="repo"></param>
        public void Connect(RepositoryRegistration repo, string applicationKey = null)
        {
            _Connect(repo, null, null, applicationKey);
        }

        /// <summary>
        /// Internal connection method used to restablish a session 
        /// </summary>
        protected void _Connect(RepositoryRegistration repo, string username, string password, string applicationKey = null)
        {
            _masterSession = new Session();
            _masterSession.AutoReconnect = false;
            _masterSession.ApplicationName = SessionName;
            _masterSession.ApplicationId = applicationKey;
            _masterSession.Connect(repo);

            if(username == null) {
                _masterSession.LogIn();
            }
            else {
                _masterSession.LogIn(username, password);
            }
            _masterSession.KeepAlive = true;
            _connectionString = _masterSession.GetSerializedConnectionString(10.2);
        }

        public Session GetSession()
        {
            if(_connectionString != null) {
                if (LockObject(LOCK_TIMEOUT)) {
                    Session workSession = Session.CreateFromSerializedLFConnectionString(_connectionString);
                    workSession.AutoReconnect = false;
                    //Something happened to the master session. This session should be removed from the pool.
                    //close the session, remove from the session pool and throw AccessDenied 9067.
                    //the retry.
                    if (CheckIsSessionValid(workSession)) {
                        return workSession;
                    }
                    else {
                        workSession.Close();
                        SessionPool.RemoveSession(SessionId);
                        UnlockObject();
                        throw new AccessDeniedException("Underlying session closed.") {
                            ErrorCode = 9067
                        };
                    }
                }
                else {
                    throw new LockedObjectException("Pooled session is already in use");
                }
            }
            else {
                //TODO: $Figure out which exception Session throws and use that
                throw new Exception("Must connect first");
            }
        }

        protected bool CheckIsSessionValid(Session workSession)
        {
            //The session is valid if it is authenticated and is the same session Id as the master
            //If the session ids don't match, this means that something happened to the master session
            //and further use could lead to orphaned sessions.
            return workSession != null && workSession.IsAuthenticated && workSession.Id == _masterSession.Id;
        }

        protected bool CheckIsMasterSessionAlive()
        {
            try {
                using (FolderInfo root = Folder.GetRootFolder(_masterSession)) { }
                return true;
            }
            catch {
                if(_connectionString != null) {
                    PooledSessionClosedEvent?.Invoke(this, null);
                }
                return false;
            }
        }

        private bool LockObject(TimeSpan timeout)
        {
            if(!_isInUse) {
                Monitor.TryEnter(_lockToken, timeout, ref _isInUse);
                return true;
            }
            else {
                return false;
            }
        }

        public void UnlockObject()
        {
            //Release the lock if locked
            if (Monitor.IsEntered(_lockToken)) {
                Monitor.Exit(_lockToken);
            }
            _isInUse = false;
        }

        public virtual void Dispose()
        {
            UnlockObject();
            if(_masterSession != null) {
                //unbind the event handler. the object is being disposed so the lock state doesn't matter
                //note that this needs to be done before we call close.
                _masterSession.Close();
            }
        }
    }
}
