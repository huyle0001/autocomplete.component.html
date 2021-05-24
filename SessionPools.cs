using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Laserfiche.SessionPool;
using Laserfiche.RepositoryAccess;
using ECM.Models;
using System.Collections.Concurrent;

namespace ECM.Utils
{
    public static class SessionPools
    {
        static ConcurrentDictionary<RepositoryInfo, SessionPool> _sessionPools = new ConcurrentDictionary<RepositoryInfo, SessionPool>();
        static ConcurrentDictionary<string, RepositoryInfo> _repositories = new ConcurrentDictionary<string, RepositoryInfo>();
        public static void Initialize(IEnumerable<RepositoryInfo> repositories)
        {
            //Call dispose to release any existing session pools
            DisposePools();

            //TODO: Update RepositoryInfo so you can specify min and max concurrency

            foreach (RepositoryInfo repo in repositories)
            {
                SessionPool sessionPool = null;

                try
                {
                    sessionPool = new SessionPool(
                    $"{repo.Respository} Pool",
                    repo.ServerName, repo.Respository,
                    repo.UserId, repo.Password, repo.InitialSize, repo.MaxSize, repo.IntegrationKey
                    );

                }
                catch (AccessDeniedException ex)
                {
                    throw ex;
                }
                finally
                {
                    if (sessionPool != null)
                    {
                        _sessionPools.TryAdd(repo, sessionPool);
                        _repositories.TryAdd(repo.Respository.ToUpper(), repo);
                    }
                }

            }
        }


        public static Session GetSession(string repositoryName)
        {
            RepositoryInfo repo;
            if(_repositories.TryGetValue(repositoryName.ToUpper(), out repo)) {
                return GetSession(repo);
            }
            else {
                throw new KeyNotFoundException($"No session pool for [{repositoryName}] found !");
            }
        }

        public static Session GetSession(RepositoryInfo repositoryInfo)
        {
            SessionPool pool;
            if (_sessionPools.TryGetValue(repositoryInfo, out pool))
            {
                return pool.GetSession();
            }
            else
            {
                throw new KeyNotFoundException($"No session pool for [{repositoryInfo.Respository}] found !");
            }
        }

        public static SessionPool GetSessionPool(string repositoryName)
        {
            RepositoryInfo repo;
            if (_repositories.TryGetValue(repositoryName, out repo)) {
                return GetSessionPool(repo);
            }
            else
            {
                throw new KeyNotFoundException($"No session pool for [{repositoryName}] found !");
            }
        }
        public static SessionPool GetSessionPool(RepositoryInfo repositoryInfo)
        {
            SessionPool pool;
            if (_sessionPools.TryGetValue(repositoryInfo, out pool)) {
                return pool;
            }
            else
            {
                throw new KeyNotFoundException($"No session pool for [{repositoryInfo.Respository}] found !");
            }
        }

        public static void ReleaseSession(string repositoryName, Session session)
        {
            RepositoryInfo repo;
            if(_repositories.TryGetValue(repositoryName.ToUpper(), out repo)) {
                ReleaseSession(repo, session);
            }
            else {
                throw new KeyNotFoundException($"No session pool for [{repositoryName}] found !");
            }
        }
        public static void ReleaseSession(RepositoryInfo repositoryInfo, Session session)
        {
            SessionPool pool;
            if (_sessionPools.TryGetValue(repositoryInfo, out pool))
            {
                pool.ReleaseSession(session);
            }
            else
            {
                throw new KeyNotFoundException($"No session pool for [{repositoryInfo.Respository}] found !");
            }
        }

        public static void DisposePools()
        {
            foreach(SessionPool p in _sessionPools.Values) {
                p.Dispose();
            }
            _sessionPools.Clear();
            _repositories.Clear();
        }
    }
}
