// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using JetBrains.Annotations;
using LiteDB;
using MaikeBing.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Update;

namespace MaikeBing.EntityFrameworkCore.NoSqLiteDB.Storage.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class LiteDBStore : ILiteDBStore
    {
        private readonly ILiteDBTableFactory _tableFactory;
        private readonly bool _useNameMatching;
        private readonly string _name;
        private readonly object _lock = new object();
        private Lazy<Dictionary<object, ILiteDBTable>> _tables;

        public LiteDatabase LiteDatabase { get; set; }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public LiteDBStore([NotNull] ILiteDBTableFactory tableFactory,string name)
            : this(tableFactory, useNameMatching: false,name)
        {
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public LiteDBStore(
            [NotNull] ILiteDBTableFactory tableFactory,
            bool useNameMatching,
            string name)
        {
            _tableFactory = tableFactory;
            LiteDatabase = new LiteDB.LiteDatabase(name);
            _tableFactory.LiteDatabase = LiteDatabase;
            _useNameMatching = useNameMatching;
            _tables = CreateTables();
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual bool EnsureCreated(
            StateManagerDependencies stateManagerDependencies,
            IDiagnosticsLogger<DbLoggerCategory.Update> updateLogger)
        {
            lock (_lock)
            {
                var valuesSeeded = !_tables.IsValueCreated;
                if (valuesSeeded)
                {
                    // ReSharper disable once AssignmentIsFullyDiscarded
                    _ = _tables.Value;

                    var stateManager = new StateManager(stateManagerDependencies);
                    var entries = new List<IUpdateEntry>();
                    foreach (var entityType in stateManagerDependencies.Model.GetEntityTypes())
                    {
                        foreach (var targetSeed in entityType.GetSeedData())
                        {
                            var entry = stateManager.CreateEntry(targetSeed, entityType);
                            entry.SetEntityState(EntityState.Added);
                            entries.Add(entry);
                        }
                    }

                    ExecuteTransaction(entries, updateLogger);
                }

                return valuesSeeded;
            }
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual bool Clear()
        {
            lock (_lock)
            {
                if (!_tables.IsValueCreated)
                {
                    return false;
                }

                _tables = CreateTables();

                return true;
            }
        }

        private Lazy<Dictionary<object, ILiteDBTable>> CreateTables() => new Lazy<Dictionary<object, ILiteDBTable>>(()=>new Dictionary<object, ILiteDBTable>());
     

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual IList<LiteDBTableSnapshot> GetTables(IEntityType entityType)
        {
            var data = new List<LiteDBTableSnapshot>();
            lock (_lock)
            {
                var keyt = _useNameMatching ? (object)entityType.Name : entityType;
                if (!_tables.IsValueCreated || !_tables.Value.ContainsKey(keyt))
                {
                    _tables.Value.Add(keyt, _tableFactory.Create(entityType));
                }
                foreach (var et in entityType.GetConcreteDerivedTypesInclusive ())
                {
                    var key = _useNameMatching ? (object)et.Name : et;
                    if (_tables.Value.TryGetValue(key, out var table))
                    {
                        data.Add(new LiteDBTableSnapshot(et, table.SnapshotRows()));
                    }
                }
            }
            return data;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual int ExecuteTransaction(
            IList<IUpdateEntry> entries,
            IDiagnosticsLogger<DbLoggerCategory.Update> updateLogger)
        {
            var rowsAffected = 0;

            lock (_lock)
            {
                // ReSharper disable once ForCanBeConvertedToForeach
                for (var i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    var entityType = entry.EntityType;

                    Debug.Assert(!entityType.IsAbstract());

                    var key = _useNameMatching ? (object)entityType.Name : entityType;
                    if (!_tables.Value.TryGetValue(key, out var table))
                    {
                        _tables.Value.Add(key, table = _tableFactory.Create(entityType));
                    }

                    if (entry.SharedIdentityEntry != null)
                    {
                        if (entry.EntityState == EntityState.Deleted)
                        {
                            continue;
                        }

                        table.Delete(entry);
                    }

                    switch (entry.EntityState)
                    {
                        case EntityState.Added:
                            table.Create(entry);
                            break;
                        case EntityState.Deleted:
                            table.Delete(entry);
                            break;
                        case EntityState.Modified:
                            table.Update(entry);
                            break;
                    }

                    rowsAffected++;
                }
            }
            updateLogger.ChangesSaved(entries, rowsAffected);
            return rowsAffected;
        }

        IReadOnlyList<LiteDBTableSnapshot> ILiteDBStore.GetTables(IEntityType entityType)
        {
            throw new NotImplementedException();
        }

    
    }
}
