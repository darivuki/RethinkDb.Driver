using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RethinkDb.Driver.Ast;
using RethinkDb.Driver.Model;

namespace RethinkDb.Driver.Net
{
    public class Connection
    {
        // public immutable
        public readonly string hostname;
        public readonly int port;

        private long nextToken = 0;
        private readonly Func<ConnectionInstance> instanceMaker;

        // private mutable
        private string dbname;
        private readonly TimeSpan? connectTimeout;
        private readonly byte[] handshake;
        private ConnectionInstance instance = null;

        internal Connection(Builder builder)
        {
            dbname = builder.dbname;
            var authKey = builder._authKey ?? string.Empty;
            var authKeyBytes = Encoding.ASCII.GetBytes(authKey);

            using( var ms = new MemoryStream() )
            using( var bw = new BinaryWriter(ms) )
            {
                bw.Write((int)RethinkDb.Driver.Proto.Version.V0_4);
                bw.Write(authKeyBytes.Length);
                bw.Write(authKeyBytes);
                bw.Write((int)RethinkDb.Driver.Proto.Protocol.JSON);
                bw.Flush();
                handshake = ms.ToArray();
            }

            hostname = builder._hostmame ?? "localhost";
            port = builder._port ?? 28015;
            connectTimeout = builder._timeout;

            instanceMaker = builder.instanceMaker;
        }

        public static Builder build()
        {
            return new Builder(() => new ConnectionInstance());
        }

        public virtual string db()
        {
            return dbname;
        }

        internal virtual void AddToCache<T>(long token, Cursor<T> cursor)
        {
            if( instance == null )
                throw new ReqlDriverError("Can't add to cache when not connected.");

            instance?.AddToCache(token, cursor);
        }

        internal virtual void RemoveFromCache(long token)
        {
            instance?.RemoveFromCache(token);
        }

        public virtual void use(string db)
        {
            dbname = db;
        }

        public virtual TimeSpan? timeout()
        {
            return connectTimeout;
        }

        public virtual Connection reconnect()
        {
            try
            {
                return reconnect(false, null);
            }
            catch( Exception e )
            {
                throw e;
            }
        }

        public virtual Connection reconnect(bool noreplyWait, TimeSpan? timeout)
        {
            if( !timeout.HasValue )
            {
                timeout = connectTimeout;
            }
            close(noreplyWait);
            ConnectionInstance inst = instanceMaker();
            instance = inst;
            inst.Connect(hostname, port, handshake, timeout);
            return this;
        }

        public virtual bool Open => instance?.Open ?? false;

        public virtual ConnectionInstance checkOpen()
        {
            if( !instance?.Open ?? true )
            {
                throw new ReqlDriverError("Connection is closed.");
            }
            return instance;
        }

        public virtual void close(bool shouldNoReplyWait = true)
        {
            if( instance != null )
            {
                try
                {
                    if( shouldNoReplyWait )
                    {
                        noreplyWait();
                    }
                }
                finally
                {
                    nextToken = 0;
                    instance.Close();
                    instance = null;
                }
            }
        }

        private long NewToken()
        {
            return Interlocked.Increment(ref nextToken);
        }

        internal virtual Response ReadResponse(Query query)
        {
            return ReadResponse(query, null);
        }

        internal virtual Response ReadResponse(Query query, long? deadline)
        {
            return checkOpen().ReadResponse(query, deadline);
        }

        internal virtual Cursor<T> RunQueryCursor<T>(Query query)
        {
            var inst = checkOpen();
            if( inst.Socket == null ) throw new ReqlDriverError("No socket open.");
            inst.Socket.WriteQuery(query.Token, query.Serialize());
            Response res = inst.ReadResponse(query);
            if( res.IsPartial || res.IsSequence )
            {
                return Cursor<T>.create(this, query, res);
            }
            throw new ReqlDriverError("The query response can't be converted to a Cursor<T>. The response is not a sequence or partial. Use `.run` instead.");
        }

        internal virtual void RunQueryNoreply(Query query)
        {
            var inst = checkOpen();
            if( inst.Socket == null ) throw new ReqlDriverError("No socket open.");
            inst.Socket.WriteQuery(query.Token, query.Serialize());
        }

        internal virtual dynamic RunQuery<T>(Query query)
        {
            var inst = checkOpen();
            if( inst.Socket == null ) throw new ReqlDriverError("No socket open.");

            inst.Socket.WriteQuery(query.Token, query.Serialize());

            Response res = inst.ReadResponse(query);

            if( res.IsAtom )
            {
                try
                {
                    return res.Data[0].ToObject(typeof(T), Converter.Seralizer);
                }
                catch( IndexOutOfRangeException ex )
                {
                    throw new ReqlDriverError("Atom response was empty!", ex);
                }
            }
            else if( res.IsPartial || res.IsSequence )
            {
                ICursor cursor = Cursor<T>.create(this, query, res);
                return cursor;
            }
            else if( res.IsWaitComplete )
            {
                return null;
            }
            else
            {
                throw res.MakeError(query);
            }
        }

        public virtual void noreplyWait()
        {
            RunQuery<object>(Query.NoReplyWait(NewToken()));
        }

        private Query PrepareQuery(ReqlAst term, OptArgs globalOpts)
        {
            SetDefaultDb(globalOpts);
            Query q = Query.Start(NewToken(), term, globalOpts);
            if( globalOpts?.ContainsKey("noreply") == true )
            {
                throw new ReqlDriverError("Don't provide the noreply option as an optarg. Use `.runNoReply` instead of `.run`");
            }
            return q;
        }

        public virtual dynamic run<T>(ReqlAst term, object globalOpts)
        {
            Query q = PrepareQuery(term, OptArgs.fromAnonType(globalOpts));
            return RunQuery<T>(q);
        }

        public virtual Cursor<T> runCursor<T>(ReqlAst term, object globalOpts)
        {
            Query q = PrepareQuery(term, OptArgs.fromAnonType(globalOpts));
            return RunQueryCursor<T>(q);
        }

        private void SetDefaultDb(OptArgs globalOpts)
        {
            if( globalOpts?.ContainsKey("db") == false && this.dbname != null )
            {
                // Only override the db global arg if the user hasn't
                // specified one already and one is specified on the connection
                globalOpts.with("db", this.dbname);
            }
            if( globalOpts?.ContainsKey("db") == true )
            {
                // The db arg must be wrapped in a db ast object
                globalOpts.with("db", new Db(Arguments.Make(globalOpts["db"])));
            }
        }

        public void runNoReply(ReqlAst term, object globalOpts)
        {
            var opts = OptArgs.fromAnonType(globalOpts);
            SetDefaultDb(opts);
            opts.with("noreply", true);
            RunQueryNoreply(Query.Start(NewToken(), term, opts));
        }

        internal virtual void Continue(ICursor cursor)
        {
            RunQueryNoreply(Query.Continue(cursor.Token));
        }

        internal virtual void Stop(ICursor cursor)
        {
            RunQueryNoreply(Query.Stop(cursor.Token));
        }


        public class Builder
        {
            internal readonly Func<ConnectionInstance> instanceMaker;
            internal string _hostmame = null;
            internal int? _port = null;
            internal string dbname = null;
            internal string _authKey = null;
            internal TimeSpan? _timeout = null;

            public Builder(Func<ConnectionInstance> instanceMaker)
            {
                this.instanceMaker = instanceMaker;
            }

            public virtual Builder hostname(string val)
            {
                this._hostmame = val;
                return this;
            }

            public virtual Builder port(int val)
            {
                this._port = val;
                return this;
            }

            public virtual Builder db(string val)
            {
                this.dbname = val;
                return this;
            }

            public virtual Builder authKey(string val)
            {
                this._authKey = val;
                return this;
            }

            public virtual Builder timeout(int val)
            {
                this._timeout = TimeSpan.FromSeconds(val);
                return this;
            }

            public virtual Connection connect()
            {
                var conn = new Connection(this);
                conn.reconnect();
                return conn;
            }
        }
    }
}
