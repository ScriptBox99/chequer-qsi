﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Qsi.Analyzers.Context;
using Qsi.Collections;
using Qsi.Data;
using Qsi.Tree;
using Qsi.Utilities;

namespace Qsi.Analyzers
{
    public abstract class QsiAnalyzerBase
    {
        protected IEqualityComparer<QsiIdentifier> IdentifierComparer => _identifierComparer.Value;

        protected IEqualityComparer<QsiQualifiedIdentifier> QualifiedIdentifierComparer => _qualifiedIdentifierComparer.Value;

        private readonly QsiEngine _engine;
        private readonly Lazy<IEqualityComparer<QsiIdentifier>> _identifierComparer;
        private readonly Lazy<IEqualityComparer<QsiQualifiedIdentifier>> _qualifiedIdentifierComparer;

        protected QsiAnalyzerBase(QsiEngine engine)
        {
            _engine = engine;
            _identifierComparer = new Lazy<IEqualityComparer<QsiIdentifier>>(() => new DelegateEqualityComparer<QsiIdentifier>(Match));
            _qualifiedIdentifierComparer = new Lazy<IEqualityComparer<QsiQualifiedIdentifier>>(() => new DelegateEqualityComparer<QsiQualifiedIdentifier>(Match));
        }

        public ValueTask<IQsiAnalysisResult> Execute(
            QsiScript script,
            QsiParameter[] parameters,
            IQsiTreeNode tree,
            QsiAnalyzerOptions options,
            CancellationToken cancellationToken = default)
        {
            if (!CanExecute(script, tree))
                throw new QsiException(QsiError.NotSupportedScript, script.ScriptType);

            return OnExecute(new AnalyzerContext(_engine, script, parameters, tree, options, cancellationToken));
        }

        public abstract bool CanExecute(QsiScript script, IQsiTreeNode tree);

        protected abstract ValueTask<IQsiAnalysisResult> OnExecute(IAnalyzerContext context);

        #region Utilities
        protected bool Match(QsiIdentifier a, QsiIdentifier b)
        {
            return _engine.LanguageService.MatchIdentifier(a, b);
        }

        protected bool Match(QsiQualifiedIdentifier a, QsiQualifiedIdentifier b)
        {
            if (a.Level != b.Level)
                return false;

            for (int i = 0; i < a.Level; i++)
            {
                if (!Match(a[i], b[i]))
                    return false;
            }

            return true;
        }

        protected bool Match(IAnalyzerContext context, QsiTableStructure table, QsiQualifiedIdentifier identifier)
        {
            if (!table.HasIdentifier)
                return false;

            // * case - Explicit access
            // ┌──────────────────────────────────────────────────────────┐
            // │ SELECT sakila.actor.column FROM sakila.actor             │
            // │        ▔▔▔▔▔▔^▔▔▔▔▔      ==     ▔▔▔▔▔▔^▔▔▔▔▔             │
            // │         └-> identifier(2)        └-> table.Identifier(2) │
            // └──────────────────────────────────────────────────────────┘ 

            if (Match(table.Identifier, identifier))
                return true;

            // * case - 2 Level implicit access
            // ┌──────────────────────────────────────────────────────────┐
            // │ SELECT actor.column FROM sakila.actor                    │
            // │        ▔▔▔▔▔      <       ▔▔▔▔▔^▔▔▔▔▔                    │
            // │         └-> identifier(1)  └-> table.Identifier(2)       │
            // └──────────────────────────────────────────────────────────┘ 

            // * case - 3 Level implicit access
            // ┌──────────────────────────────────────────────────────────┐
            // │ SELECT sakila.actor.column FROM db.sakila.actor          │
            // │        ▔▔▔▔▔▔^▔▔▔▔▔       <     ▔▔^▔▔▔▔▔▔^▔▔▔▔▔          │
            // │         └-> identifier(2)        └-> table.Identifier(3) │
            // └──────────────────────────────────────────────────────────┘ 

            if (context.Options.UseExplicitRelationAccess)
                return false;

            if (!QsiUtility.IsReferenceType(table.Type))
                return false;

            if (table.Identifier.Level <= identifier.Level)
                return false;

            QsiIdentifier[] partialIdentifiers = table.Identifier[^identifier.Level..];
            var partialIdentifier = new QsiQualifiedIdentifier(partialIdentifiers);

            return Match(partialIdentifier, identifier);
        }
        #endregion

        protected readonly struct DelegateEqualityComparer<T> : IEqualityComparer<T>
        {
            private readonly Func<T, T, bool> _equalsDelegate;

            public DelegateEqualityComparer(Func<T, T, bool> equalsDelegate)
            {
                _equalsDelegate = equalsDelegate;
            }

            public bool Equals(T x, T y)
            {
                return _equalsDelegate(x, y);
            }

            public int GetHashCode(T obj)
            {
                return obj.GetHashCode();
            }
        }
    }
}
