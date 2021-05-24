using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Laserfiche.RepositoryAccess;

namespace Laserfiche.SessionPool
{
    public class SessionPool : IDisposable
    {
        private TimeSpan _lockTimeout = TimeSpan.FromMilliseconds(2500);
        private TimeSpan _retryDelay = TimeSpan.FromMilliseconds(2500);
        private int _retryLimit = 3;
        private int _target = 10;
        protected List<PooledSession> _sessions;
        protected ConcurrentQueue<PooledSession> _avaliableSessions;
        private RepositoryRegistration _repo;
        private string _applicationKey;
        private string _username;
        private string _password;
        private object _lockToken = new object();

        public int MaxSessionCount                              { get; private set; }
        public string Name                                      { get; private set; }
        public int SessionCount                                 { get => _sessions.Count(); }
        public int SessionsInUse                                { get => _sessions.Where(s => s.IsInUse).Count(); }
        protected bool IsExpanding                              { get => Monitor.IsEntered(_lockToken); }

        /// <summary>
        /// The number retries that should be attempted when calling GetSession()
        /// </summary>
        public int RetryLimit                                   { get => _retryLimit; set => _retryLimit = value; }

        /// <summary>
        /// How long the thread should wait for the lock to become avaliable
        /// </summary>
        public TimeSpan LockTimeout                             { get => _lockTimeout; set => _lockTimeout = value; }

        /// <summary>
        /// The amount of time to wait between retry attempts
        /// </summary>
        public TimeSpan RetryDelay                              { get => _retryDelay; set => _retryDelay = value; }

        public int Target { get => _target; set => _target = value; }
        public SessionPool(
            string name,
            string server, 
            string repository, 
            string username, 
            string password, 
            int initialSize, 
            int maxSize, 
            string applicationKey = null)
        {
            RepositoryRegistration repo = new RepositoryRegistration(server, repository);
            Initialize(name, repo, username, password, initialSize, maxSize, applicationKey);
        }

        public SessionPool(
            string name, 
            RepositoryRegistration repo,
            string username, 
            string password,
            int initialSize,
            int maxSize,
            string applicationKey = null)
        {
            Initialize(name, repo, username, password, initialSize, maxSize, applicationKey);
        }

        public static bool CheckIsSessionValid(Session session)
        {
            if(!session.IsAuthenticated) {
                return false;
            }
            else {
                try {
                    using (FolderInfo root = Folder.GetRootFolder(session)) {
                        return true;
                    }
                }
                catch {
                    return false;
                }
            }
        }

        public bool IsSessionInUse(Session session)
        {
            PooledSession ps = _sessions.FirstOrDefault(s => s.SessionId == session.Id);
            return ps?.IsInUse ?? false;
        }

        /// <summary>
        /// Removes a session from the session pool, and closes the underlying session.
        /// This should only be used to handle AccessDeniedException [9067] errors where the session
        /// object is terminated externally.
        /// </summary>
        /// <param name="id"></param>
        public void RemoveSession(int id)
        {
            foreach(PooledSession ps in _sessions.Where(p => p.SessionId == id)) {
                _sessions.Remove(ps);
            }
        }

        public Session GetSession(int retryCount = 0)
        {
            //If the we are under the retry limit try and get a session
            if(retryCount <= _retryLimit) {
                PooledSession ps;
                retryCount++;
                if (TryGetNextAvaliableSession(out ps))
                {
                    //Try and return the session. If it's continue retry loop.
                    try
                    {
                        return ps.GetSession();
                    }
                    catch (LockedObjectException)
                    {
                        return GetSession(retryCount);
                    }
                }
                else
                {
                    //Try and expand the session pool
                    int expansionCount = CanExpandSessionPoolBy(Target);
                    if (expansionCount > 0)
                    {
                        ExpandSessionPool(expansionCount);
                    }
                    //Sleep thread to allow a session to become avaliable

                    //Thread.Sleep(RetryDelay);

                    Task.Delay(RetryDelay);

                    return GetSession(retryCount);
                }
            }
            else {
                throw new MaxRetryException();
            }
        }

        /// <summary>
        /// Releases a session back to the session pool.
        /// </summary>
        /// <param name="session"></param>
        public void ReleaseSession(Session session)
        {
            PooledSession ps = _sessions.FirstOrDefault(s => s.SessionId == session.Id);
            if(ps != null && ps.IsInUse) {
                ps.UnlockObject();
                _avaliableSessions.Enqueue(ps);
            }
        }

        public void Dispose()
        {
            if (_sessions != null)
            {
                foreach (PooledSession ps in _sessions)
                {
                    //Since we're disposing the pool, we don't need to handle the session close events
                    ps.PooledSessionClosedEvent -= PooledSession_CloseEventHandler;
                    ps.Dispose();
                }
            }
        }



        #region Helper Methods
        private void ExpandSessionPool(int count)
        {
            if(Monitor.TryEnter(_lockToken, _lockTimeout)) {
                try {
                    for(int i = 0; i < count; i++) {
                        PooledSession session = CreatePooledSession();
                        _sessions.Add(session);
                        _avaliableSessions.Enqueue(session);
                    }
                }
                finally {
                    Monitor.Exit(_lockToken);
                }
            }
        }

        private PooledSession CreatePooledSession()
        {
            PooledSession ps = new PooledSession(Name, this);
            ps.Connect(_repo, _username, _password, _applicationKey);
            ps.PooledSessionClosedEvent += PooledSession_CloseEventHandler;
            return ps;
        }


        private void Initialize(string name, RepositoryRegistration repo, string username, string password, 
            int initialSize, int maxSize, string applicationKey) 
        {
            _repo = repo;
            _applicationKey = applicationKey;
            _username = username;
            _password = password;
            MaxSessionCount = maxSize;
            Name = name;
            _sessions = new List<PooledSession>();
            _avaliableSessions = new ConcurrentQueue<PooledSession>();

            //Initialize threadpool with sessions
            if(initialSize > maxSize) {
                throw new ArgumentException("Initial size must be less than or equal to Max Size");
            }

            try {
                ExpandSessionPool(initialSize);
            }
            catch {
                //If initialization fails for any reason, there may be orphaned connections. Call dispose to clean them up
                this.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Determines if the session pool can expand, and returns back the allowed size.
        /// </summary>
        /// <param name="target"></param>
        /// <returns></returns>
        protected int CanExpandSessionPoolBy(int target)
        {
            if(!IsExpanding) {
               if(SessionCount + target <= MaxSessionCount) {
                    return target;
                }
               else {
                    return MaxSessionCount - SessionCount;
                }
            }
            else {
                return 0;
            }
        }

        protected bool TryGetNextAvaliableSession(out PooledSession session)
        {
            //Pops off the avalibe session stack and checks that the session is alive.
            //If the stack is exhausted returns false;
            while (_avaliableSessions.TryDequeue(out session)) {
                //MUST CALL session.IsAlive. This triggers session removal from the pool
                //in the event that the session is closed.
                if(session.IsAlive) {
                    return true;
                }
            }
            //No avaliable sessions
            return false;
        }
        #endregion

        #region Event Handlers
        /// <summary>
        /// Removes the session from the pool in the event that it is terminated or closed.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void PooledSession_CloseEventHandler(object sender, EventArgs e)
        {
            PooledSession ps = (PooledSession)sender;
            _sessions.Remove(ps);
        }
        #endregion
    }
}
