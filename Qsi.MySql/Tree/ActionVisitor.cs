﻿using System.Collections.Generic;
using System.Linq;
using Qsi.Data;
using Qsi.MySql.Tree.Common;
using Qsi.Tree;
using Qsi.Utilities;
using static Qsi.MySql.Internal.MySqlParser;

namespace Qsi.MySql.Tree
{
    internal static class ActionVisitor
    {
        #region Prepared
        public static QsiActionNode VisitPreparedStatement(PreparedStatementContext context)
        {
            switch (context.children[0])
            {
                case PrepareStatementContext prepareStatementContext:
                    return VisitPrepareStatement(prepareStatementContext);

                case ExecuteStatementContext executeStatementContext:
                    return VisitExecuteStatement(executeStatementContext);

                case DeallocatePrepareContext deallocatePrepareContext:
                    return VisitDeallocatePrepare(deallocatePrepareContext);
            }

            return null;
        }

        public static QsiActionNode VisitPrepareStatement(PrepareStatementContext context)
        {
            return TreeHelper.Create<QsiPrepareActionNode>(n =>
            {
                n.Identifier = IdentifierVisitor.Visit(context.uid());

                if (context.query != null)
                {
                    n.Query.SetValue(new QsiLiteralExpressionNode
                    {
                        Value = IdentifierUtility.Unescape(context.query.Text),
                        Type = QsiDataType.String
                    });
                }
                else if (context.variable != null)
                {
                    n.Query.SetValue(ExpressionVisitor.VisitLocalId(context.LOCAL_ID()));
                }
            });
        }

        public static QsiActionNode VisitDeallocatePrepare(DeallocatePrepareContext context)
        {
            return TreeHelper.Create<QsiDropPrepareActionNode>(n =>
            {
                n.Identifier = IdentifierVisitor.Visit(context.uid());
            });
        }

        public static QsiActionNode VisitExecuteStatement(ExecuteStatementContext context)
        {
            return TreeHelper.Create<QsiExecutePrepareActionNode>(n =>
            {
                n.Identifier = IdentifierVisitor.Visit(context.uid());

                if (context.userVariables() != null)
                {
                    n.Variables.SetValue(ExpressionVisitor.VisitUserVariables(context.userVariables()));
                }
            });
        }
        #endregion

        #region Insert, Replace
        public static QsiActionNode VisitInsertStatement(InsertStatementContext context)
        {
            return VisitInsertStatement(new CommonInsertStatementContext(context));
        }

        public static QsiActionNode VisitReplaceStatement(ReplaceStatementContext context)
        {
            return VisitInsertStatement(new CommonInsertStatementContext(context));
        }

        private static QsiActionNode VisitInsertStatement(CommonInsertStatementContext context)
        {
            var node = new QsiDataInsertActionNode
            {
                ConflictBehavior = context.ConflictBehavior
            };

            if (!ListUtility.IsNullOrEmpty(context.Partitions))
            {
                node.Partitions = context.Partitions
                    .Select(uid => new QsiQualifiedIdentifier(IdentifierVisitor.VisitUid(uid)))
                    .ToArray();
            }

            if (!ListUtility.IsNullOrEmpty(context.Columns))
            {
                IEnumerable<QsiDeclaredColumnNode> columns = context.Columns
                    .Select(uid => new QsiDeclaredColumnNode
                    {
                        Name = new QsiQualifiedIdentifier(IdentifierVisitor.VisitUid(uid))
                    });

                node.Columns.SetValue(TreeHelper.Create<QsiColumnsDeclarationNode>(dn =>
                {
                    dn.Columns.AddRange(columns);
                }));
            }

            if (context.InsertStatementValue != null)
            {
                var value = context.InsertStatementValue;

                if (value.selectStatement() != null)
                {
                    node.ValueTable.SetValue(TableVisitor.VisitSelectStatement(value.selectStatement()));
                }
                else
                {
                    IEnumerable<QsiRowValueExpressionNode> rows = value.expressionsWithDefaults()
                        .Select(ExpressionVisitor.VisitExpressionsWithDefaults);

                    node.Values.AddRange(rows);
                }
            }
            else if (!ListUtility.IsNullOrEmpty(context.SetElements))
            {
                node.SetValues.AddRange(context.SetElements.Select(ExpressionVisitor.VisitUpdatedElement));
            }
            else
            {
                throw new QsiException(QsiError.Syntax);
            }

            if (!ListUtility.IsNullOrEmpty(context.DuplicateSetElements))
            {
                var action = new QsiDataConflictActionNode();
                action.SetValues.AddRange(context.DuplicateSetElements.Select(ExpressionVisitor.VisitUpdatedElement));

                node.ConflictAction.SetValue(action);
            }

            return node;
        }
        #endregion
    }
}
