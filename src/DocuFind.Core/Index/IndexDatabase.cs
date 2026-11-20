using System.Globalization;
using System.Runtime.InteropServices;
using DocuFind.Core.Text;
using Microsoft.Data.Sqlite;

namespace DocuFind.Core.Index;

public sealed record DocumentRecord(
    long Id,
    string Path,
    string FileName,
    string NameKey,
    string Hash,
    long Size,
    long ModifiedUtc,
    int PageCount,
    int ChunkCount,
    long AddedAt);

public sealed record ChunkRow(long Id, long DocId, int Ordinal, int Page, int CharStart, string Text);

/// <summary>SQLite 索引库：文档元数据 + 切片正文 + FTS5(trigram) 全文索引 + 向量 BLOB。</summary>
public sealed class IndexDatabase : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly object _gate = new();

    public IndexDatabase(string path)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            Cache = SqliteCacheMode.Private
        };

        _connection = new SqliteConnection(builder.ToString());
        _connection.Open();
        Execute("PRAGMA journal_mode=WAL;");
        Execute("PRAGMA synchronous=NORMAL;");
        Execute("PRAGMA temp_store=MEMORY;");
        InitializeSchema();
    }

    private void InitializeSchema()
    {
        Execute("""
            CREATE TABLE IF NOT EXISTS docs (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                path        TEXT    NOT NULL,
                file_name   TEXT    NOT NULL,
                name_key    TEXT    NOT NULL,
                hash        TEXT    NOT NULL,
                size        INTEGER NOT NULL,
                mtime       INTEGER NOT NULL,
                page_count  INTEGER NOT NULL DEFAULT 0,
                chunk_count INTEGER NOT NULL DEFAULT 0,
                added_at    INTEGER NOT NULL
            );
            """);

        Execute("CREATE UNIQUE INDEX IF NOT EXISTS ux_docs_hash ON docs(hash);");
        Execute("CREATE INDEX IF NOT EXISTS ix_docs_name ON docs(name_key);");

        Execute("""
            CREATE TABLE IF NOT EXISTS chunks (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                doc_id     INTEGER NOT NULL,
                ordinal    INTEGER NOT NULL,
                page       INTEGER NOT NULL,
                char_start INTEGER NOT NULL,
                text       TEXT    NOT NULL,
                vector     BLOB
            );
            """);

        Execute("CREATE INDEX IF NOT EXISTS ix_chunks_doc ON chunks(doc_id);");

        Execute("CREATE VIRTUAL TABLE IF NOT EXISTS chunks_fts USING fts5(body, tokenize='trigram');");

        Execute("""
            CREATE TABLE IF NOT EXISTS meta (
                key   TEXT PRIMARY KEY,
                value TEXT
            );
            """);
    }

    public string? GetMeta(string key)
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT value FROM meta WHERE key = $k";
            cmd.Parameters.AddWithValue("$k", key);
            return cmd.ExecuteScalar() as string;
        }
    }

    public void SetMeta(string key, string value)
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "INSERT INTO meta(key, value) VALUES($k, $v) ON CONFLICT(key) DO UPDATE SET value = excluded.value";
            cmd.Parameters.AddWithValue("$k", key);
            cmd.Parameters.AddWithValue("$v", value);
            cmd.ExecuteNonQuery();
        }
    }

    public DocumentRecord? FindByHash(string hash)
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"{SelectDocument} WHERE hash = $h LIMIT 1";
            cmd.Parameters.AddWithValue("$h", hash);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? ReadDocument(reader) : null;
        }
    }

    public DocumentRecord? FindByName(string fileName)
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"{SelectDocument} WHERE name_key = $n LIMIT 1";
            cmd.Parameters.AddWithValue("$n", NormalizeName(fileName));
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? ReadDocument(reader) : null;
        }
    }

    public long InsertDocument(string path, string fileName, string hash, long size, long modifiedUtc, int pageCount)
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO docs(path, file_name, name_key, hash, size, mtime, page_count, chunk_count, added_at)
                VALUES($p, $f, $n, $h, $s, $m, $pc, 0, $a)
                RETURNING id;
                """;
            cmd.Parameters.AddWithValue("$p", path);
            cmd.Parameters.AddWithValue("$f", fileName);
            cmd.Parameters.AddWithValue("$n", NormalizeName(fileName));
            cmd.Parameters.AddWithValue("$h", hash);
            cmd.Parameters.AddWithValue("$s", size);
            cmd.Parameters.AddWithValue("$m", modifiedUtc);
            cmd.Parameters.AddWithValue("$pc", pageCount);
            cmd.Parameters.AddWithValue("$a", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
    }

    /// <summary>写入切片（含向量与全文索引），返回新切片 id。</summary>
    public List<long> InsertChunks(long docId, IReadOnlyList<TextChunk> chunks, IReadOnlyList<float[]?>? vectors)
    {
        var ids = new List<long>(chunks.Count);

        lock (_gate)
        {
            using var transaction = _connection.BeginTransaction();

            using var insertChunk = _connection.CreateCommand();
            insertChunk.Transaction = transaction;
            insertChunk.CommandText = """
                INSERT INTO chunks(doc_id, ordinal, page, char_start, text, vector)
                VALUES($d, $o, $p, $c, $t, $v)
                RETURNING id;
                """;
            var pDoc = insertChunk.Parameters.Add("$d", SqliteType.Integer);
            var pOrdinal = insertChunk.Parameters.Add("$o", SqliteType.Integer);
            var pPage = insertChunk.Parameters.Add("$p", SqliteType.Integer);
            var pCharStart = insertChunk.Parameters.Add("$c", SqliteType.Integer);
            var pText = insertChunk.Parameters.Add("$t", SqliteType.Text);
            var pVector = insertChunk.Parameters.Add("$v", SqliteType.Blob);

            using var insertFts = _connection.CreateCommand();
            insertFts.Transaction = transaction;
            insertFts.CommandText = "INSERT INTO chunks_fts(rowid, body) VALUES($id, $b)";
            var pId = insertFts.Parameters.Add("$id", SqliteType.Integer);
            var pBody = insertFts.Parameters.Add("$b", SqliteType.Text);

            for (var i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                pDoc.Value = docId;
                pOrdinal.Value = chunk.Ordinal;
                pPage.Value = chunk.Page;
                pCharStart.Value = chunk.CharStart;
                pText.Value = chunk.Text;

                var vector = vectors is not null && i < vectors.Count ? vectors[i] : null;
                pVector.Value = vector is null ? DBNull.Value : ToBytes(vector);

                var id = Convert.ToInt64(insertChunk.ExecuteScalar(), CultureInfo.InvariantCulture);
                ids.Add(id);

                pId.Value = id;
                pBody.Value = chunk.Text;
                insertFts.ExecuteNonQuery();
            }

            using var update = _connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE docs SET chunk_count = $c WHERE id = $d";
            update.Parameters.AddWithValue("$c", chunks.Count);
            update.Parameters.AddWithValue("$d", docId);
            update.ExecuteNonQuery();

            transaction.Commit();
        }

        return ids;
    }

    public void DeleteDocument(long docId)
    {
        lock (_gate)
        {
            using var transaction = _connection.BeginTransaction();

            using var fts = _connection.CreateCommand();
            fts.Transaction = transaction;
            fts.CommandText = "DELETE FROM chunks_fts WHERE rowid IN (SELECT id FROM chunks WHERE doc_id = $d)";
            fts.Parameters.AddWithValue("$d", docId);
            fts.ExecuteNonQuery();

            using var chunks = _connection.CreateCommand();
            chunks.Transaction = transaction;
            chunks.CommandText = "DELETE FROM chunks WHERE doc_id = $d";
            chunks.Parameters.AddWithValue("$d", docId);
            chunks.ExecuteNonQuery();

            using var docs = _connection.CreateCommand();
            docs.Transaction = transaction;
            docs.CommandText = "DELETE FROM docs WHERE id = $d";
            docs.Parameters.AddWithValue("$d", docId);
            docs.ExecuteNonQuery();

            transaction.Commit();
        }
    }

    public List<DocumentRecord> GetDocuments()
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"{SelectDocument} ORDER BY added_at DESC, id DESC";
            using var reader = cmd.ExecuteReader();
            var list = new List<DocumentRecord>();
            while (reader.Read()) list.Add(ReadDocument(reader));
            return list;
        }
    }

    public Dictionary<long, DocumentRecord> GetDocumentsByIds(IReadOnlyCollection<long> ids)
    {
        var result = new Dictionary<long, DocumentRecord>();
        if (ids.Count == 0) return result;

        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"{SelectDocument} WHERE id IN ({string.Join(',', ids)})";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var record = ReadDocument(reader);
                result[record.Id] = record;
            }
        }

        return result;
    }

    public List<long> GetChunkIds(long docId)
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT id FROM chunks WHERE doc_id = $d";
            cmd.Parameters.AddWithValue("$d", docId);
            using var reader = cmd.ExecuteReader();
            var ids = new List<long>();
            while (reader.Read()) ids.Add(reader.GetInt64(0));
            return ids;
        }
    }

    public Dictionary<long, ChunkRow> GetChunks(IReadOnlyCollection<long> ids)    {
        var result = new Dictionary<long, ChunkRow>();
        if (ids.Count == 0) return result;

        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"SELECT id, doc_id, ordinal, page, char_start, text FROM chunks WHERE id IN ({string.Join(',', ids)})";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var row = new ChunkRow(
                    reader.GetInt64(0),
                    reader.GetInt64(1),
                    reader.GetInt32(2),
                    reader.GetInt32(3),
                    reader.GetInt32(4),
                    reader.GetString(5));
                result[row.Id] = row;
            }
        }

        return result;
    }

    /// <summary>全文检索：≥3 字的词走 FTS5 trigram，更短的词回退 LIKE。</summary>
    public List<long> SearchKeyword(IReadOnlyList<string> terms, int limit)
    {
        var ordered = new List<long>();
        var seen = new HashSet<long>();

        var longTerms = terms.Where(t => t.Length >= 3).ToList();
        var shortTerms = terms.Where(t => t.Length is > 0 and < 3).ToList();

        lock (_gate)
        {
            if (longTerms.Count > 0)
            {
                var match = string.Join(" OR ", longTerms.Select(t => "\"" + t.Replace("\"", "\"\"") + "\""));
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = """
                    SELECT rowid FROM chunks_fts
                    WHERE chunks_fts MATCH $q
                    ORDER BY bm25(chunks_fts)
                    LIMIT $n;
                    """;
                cmd.Parameters.AddWithValue("$q", match);
                cmd.Parameters.AddWithValue("$n", limit);

                try
                {
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read() && ordered.Count < limit)
                    {
                        var id = reader.GetInt64(0);
                        if (seen.Add(id)) ordered.Add(id);
                    }
                }
                catch (SqliteException)
                {
                    // 查询语法异常时忽略关键词通道，向量通道仍然有效
                }
            }

            foreach (var term in shortTerms)
            {
                if (ordered.Count >= limit) break;

                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT id FROM chunks WHERE text LIKE $p LIMIT $n";
                cmd.Parameters.AddWithValue("$p", "%" + term + "%");
                cmd.Parameters.AddWithValue("$n", limit);

                using var reader = cmd.ExecuteReader();
                while (reader.Read() && ordered.Count < limit)
                {
                    var id = reader.GetInt64(0);
                    if (seen.Add(id)) ordered.Add(id);
                }
            }
        }

        return ordered;
    }

    /// <summary>把全部向量读进内存，供暴力余弦检索使用。</summary>
    public (long[] Ids, float[] Data, int Count, int Dimensions) LoadVectors()
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT id, vector FROM chunks WHERE vector IS NOT NULL";
            using var reader = cmd.ExecuteReader();

            var ids = new List<long>(1024);
            var data = new List<float>(1024 * 512);
            var dimensions = 0;

            while (reader.Read())
            {
                if (reader.IsDBNull(1)) continue;
                var bytes = (byte[])reader[1];
                if (bytes.Length < 4) continue;

                var floats = MemoryMarshal.Cast<byte, float>(bytes);
                if (dimensions == 0) dimensions = floats.Length;
                if (floats.Length != dimensions) continue;

                ids.Add(reader.GetInt64(0));
                data.AddRange(floats.ToArray());
            }

            return (ids.ToArray(), data.ToArray(), ids.Count, dimensions);
        }
    }

    public (int Documents, int Chunks) GetCounts()
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT (SELECT COUNT(*) FROM docs), (SELECT COUNT(*) FROM chunks)";
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? (reader.GetInt32(0), reader.GetInt32(1)) : (0, 0);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            Execute("DELETE FROM chunks_fts;");
            Execute("DELETE FROM chunks;");
            Execute("DELETE FROM docs;");
            Execute("VACUUM;");
        }
    }

    private const string SelectDocument = """
        SELECT id, path, file_name, name_key, hash, size, mtime, page_count, chunk_count, added_at FROM docs
        """;

    private static DocumentRecord ReadDocument(SqliteDataReader reader) => new(
        reader.GetInt64(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetInt64(5),
        reader.GetInt64(6),
        reader.GetInt32(7),
        reader.GetInt32(8),
        reader.GetInt64(9));

    public static string NormalizeName(string fileName) => fileName.Trim().ToLowerInvariant();

    private static byte[] ToBytes(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        MemoryMarshal.AsBytes(vector.AsSpan()).CopyTo(bytes);
        return bytes;
    }

    private void Execute(string sql)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _connection.Dispose();
        }
    }
}
