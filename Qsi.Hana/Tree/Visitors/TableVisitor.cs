﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using Qsi.Data;
using Qsi.Shared.Extensions;
using Qsi.Shared.Utilities;
using Qsi.Tree;
using Qsi.Utilities;
using static Qsi.Hana.Internal.HanaParserInternal;

namespace Qsi.Hana.Tree.Visitors
{
    internal static class TableVisitor
    {
        public static HanaDerivedTableNode VisitSelectStatement(SelectStatementContext context)
        {
            var withClause = context.withClause();
            var subquery = context.subquery();
            var forClause = context.forClause();
            var timeTravel = context.timeTravel();
            var hintClause = context.hintClause();

            var subqueryNode = VisitSubquery(subquery);

            if (withClause is not null)
                subqueryNode.Directives.SetValue(VisitWithClause(withClause));

            if (forClause is not null)
                subqueryNode.Behavior.SetValue(VisitForClause(forClause));

            if (timeTravel is not null)
                subqueryNode.TimeTravel = timeTravel.GetInputText();

            if (hintClause is not null)
                subqueryNode.Hint = hintClause.GetInputText();

            return subqueryNode;
        }

        public static QsiTableDirectivesNode VisitWithClause(WithClauseContext context)
        {
            var node = new QsiTableDirectivesNode();

            node.Tables.AddRange(context._elements.Select(VisitWithListElement));

            HanaTree.PutContextSpan(node, context);

            return node;
        }

        public static QsiDerivedTableNode VisitWithListElement(WithListElementContext context)
        {
            var node = new QsiDerivedTableNode();

            node.Alias.SetValue(new QsiAliasNode
            {
                Name = IdentifierVisitor.VisitIdentifier(context.name)
            });

            var columnListClause = context.columnListClause();

            node.Columns.SetValue(
                columnListClause != null ?
                    VisitColumnListClause(columnListClause, QsiSequentialColumnType.Default) :
                    TreeHelper.CreateAllColumnsDeclaration()
            );

            node.Source.SetValue(VisitSubquery(context.subquery()));

            HanaTree.PutContextSpan(node, context);

            return node;
        }

        public static QsiColumnsDeclarationNode VisitColumnListClause(ColumnListClauseContext context, QsiSequentialColumnType? sequence)
        {
            var node = VisitColumnList(context.list, sequence);
            HanaTree.PutContextSpan(node, context);
            return node;
        }

        public static QsiColumnsDeclarationNode VisitColumnList(ColumnListContext context, QsiSequentialColumnType? sequence)
        {
            var node = new QsiColumnsDeclarationNode();

            node.Columns.AddRange(context._columns.Select(c => VisitColumnName(c, sequence)));
            HanaTree.PutContextSpan(node, context);

            return node;
        }

        public static QsiColumnNode VisitColumnName(ColumnNameContext context, QsiSequentialColumnType? sequence = null)
        {
            QsiColumnNode node;

            if (sequence.HasValue)
            {
                node = new QsiSequentialColumnNode
                {
                    ColumnType = sequence.Value,
                    Alias =
                    {
                        Value = new QsiAliasNode
                        {
                            Name = IdentifierVisitor.VisitColumnName(context)
                        }
                    }
                };
            }
            else
            {
                node = new QsiColumnReferenceNode
                {
                    Name = new QsiQualifiedIdentifier(IdentifierVisitor.VisitColumnName(context))
                };
            }

            HanaTree.PutContextSpan(node, context);

            return node;
        }

        public static QsiColumnNode VisitFieldName(FieldNameContext context)
        {
            var node = new QsiColumnReferenceNode
            {
                Name = new QsiQualifiedIdentifier(IdentifierVisitor.VisitFieldName(context))
            };

            HanaTree.PutContextSpan(node, context);

            return node;
        }

        public static HanaDerivedTableNode VisitSubquery(SubqueryContext context)
        {
            if (context.inner != null)
                return VisitSelectStatement(context.inner);

            var node = new HanaDerivedTableNode();

            node.Columns.SetValue(VisitSelectClause(node, context.select));
            node.Source.SetValue(VisitFromClause(context.from));

            if (context.where != null)
                node.Where.SetValue(VisitWhereClause(context.where));

            if (context.groupBy != null)
                node.Grouping.SetValue(VisitGroupByClause(context.groupBy));

            // TODO: set

            if (context.orderBy != null)
                node.Order.SetValue(ExpressionVisitor.VisitOrderByClause(context.orderBy));

            if (context.limit != null)
                node.Limit.SetValue(ExpressionVisitor.VisitLimitClause(context.limit));

            HanaTree.PutContextSpan(node, context);

            return node;
        }

        public static QsiColumnsDeclarationNode VisitSelectClause(HanaDerivedTableNode tableNode, SelectClauseContext context)
        {
            var offset = 1;
            var topClause = context.topClause();

            if (topClause != null)
            {
                tableNode.Top = VisitTopClause(topClause);
                offset++;
            }

            switch (context.children[offset])
            {
                case ITerminalNode { Symbol: { Type: K_ALL } }:
                    tableNode.Operation = HanaResultSetOperation.All;
                    break;

                case ITerminalNode { Symbol: { Type: K_DISTINCT } }:
                    tableNode.Operation = HanaResultSetOperation.Distinct;
                    break;
            }

            return VisitSelectList(context.selectList());
        }

        public static QsiColumnsDeclarationNode VisitSelectList(SelectListContext context)
        {
            var node = new QsiColumnsDeclarationNode();

            node.Columns.AddRange(context._items.Select(VisitSelectItem));
            HanaTree.PutContextSpan(node, context);

            return node;
        }

        public static QsiColumnNode VisitSelectItem(SelectItemContext context)
        {
            switch (context)
            {
                case ExprItemContext exprItem:
                    return VisitExprItem(exprItem);

                case AssociationExprItemContext associationExprItem:
                    return VisitAssociationExprItem(associationExprItem);

                case WildcardItemContext wildcardItem:
                    return VisitWildcardItem(wildcardItem);

                default:
                    throw TreeHelper.NotSupportedTree(context);
            }
        }

        public static QsiColumnNode VisitExprItem(ExprItemContext context)
        {
            var expressionNode = ExpressionVisitor.VisitExpression(context.expression());
            var aliasNode = context.alias() != null ? VisitAlias(context.alias()) : null;
            var node = CreateSelectItem(context.expression(), expressionNode, aliasNode);

            HanaTree.PutContextSpan(node, context);

            return node;
        }

        public static QsiColumnNode VisitAssociationExprItem(AssociationExprItemContext context)
        {
            var expressionNode = ExpressionVisitor.VisitAssociationExpression(context.associationExpression());
            var aliasNode = context.alias() != null ? VisitAlias(context.alias()) : null;
            var node = CreateSelectItem(context.associationExpression(), expressionNode, aliasNode);

            HanaTree.PutContextSpan(node, context);

            return node;
        }

        public static QsiColumnNode CreateSelectItem(ParserRuleContext context, QsiExpressionNode expressionNode, QsiAliasNode aliasNode)
        {
            if (expressionNode is QsiColumnExpressionNode columnExpression)
            {
                if (aliasNode == null)
                    return columnExpression.Column.Value;

                var node = new QsiDerivedColumnNode();

                node.Alias.SetValue(aliasNode);
                node.Column.SetValue(columnExpression.Column.Value);

                return node;
            }
            else
            {
                var node = new QsiDerivedColumnNode();

                if (aliasNode != null)
                    node.Alias.SetValue(aliasNode);
                else
                    node.InferredName = GetInferredName(context);

                node.Expression.SetValue(expressionNode);

                return node;
            }
        }

        public static QsiColumnNode VisitWildcardItem(WildcardItemContext context)
        {
            var node = new QsiAllColumnNode();
            var tableName = context.tableName();

            if (tableName != null)
                node.Path = IdentifierVisitor.VisitTableName(tableName);

            HanaTree.PutContextSpan(node, context);

            return node;
        }

        public static ulong VisitTopClause(TopClauseContext context)
        {
            return ulong.Parse(context.top.Text);
        }

        public static QsiTableNode VisitFromClause(FromClauseContext context)
        {
            QsiTableNode[] sources = context._tables
                .Select(VisitTableExpression)
                .ToArray();

            if (sources.Length == 1)
                return sources[0];

            var anchor = sources[0];

            for (int i = 1; i < sources.Length; i++)
            {
                var join = new QsiJoinedTableNode
                {
                    IsComma = true
                };

                join.Left.SetValue(anchor);
                join.Right.SetValue(sources[i]);

                var leftSpan = HanaTree.Span[join.Left.Value];
                var rightSpan = HanaTree.Span[join.Right.Value];

                HanaTree.Span[join] = new Range(leftSpan.Start, rightSpan.End);

                anchor = join;
            }

            return anchor;
        }

        #region TableExpression
        public static QsiTableNode VisitTableExpression(TableExpressionContext context)
        {
            var child = context.children[0];

            switch (child)
            {
                case TableRefContext tableRef:
                {
                    var left = VisitTableRef(tableRef);

                    if (context.crossJoin != null)
                    {
                        var node = new QsiJoinedTableNode
                        {
                            Left = { Value = left },
                            Right = { Value = VisitTableRef(context.crossJoin) },
                            JoinType = "CROSS JOIN"
                        };

                        HanaTree.PutContextSpan(node, context);

                        return node;
                    }

                    return left;
                }

                case SubqueryTableExpressionContext subqueryTableExpression:
                    return VisitSubqueryTableExpression(subqueryTableExpression);

                case CaseJoinContext caseJoin:
                    return VisitCaseJoin(caseJoin);

                case LateralTableExpressionContext lateralTableExpression:
                    return VisitLateralTableExpression(lateralTableExpression);

                case CollectionDerivedTableContext collectionDerivedTable:
                    return VisitCollectionDerivedTable(collectionDerivedTable);

                case TableFunctionExpressionContext tableFunctionExpression:
                    return VisitTableFunctionExpression(tableFunctionExpression);

                case VariableTableContext variableTable:
                    return VisitVariableTable(variableTable);

                case AssociationTableExpressionContext associationTableExpression:
                    return VisitAssociationTableExpression(associationTableExpression);

                case TableExpressionContext:
                {
                    var node = new QsiJoinedTableNode
                    {
                        JoinType = StringUtility.JoinNotNullOrEmpty(
                            " ",
                            context.joinType()?.GetText(),
                            context.joinCardinality()?.GetText(),
                            "JOIN"
                        )
                    };

                    node.Left.SetValue(VisitTableExpression(context.left));
                    node.Right.SetValue(VisitTableExpression(context.right));

                    HanaTree.PutContextSpan(node, context);

                    return node;
                }

                default:
                    throw TreeHelper.NotSupportedTree(child);
            }
        }

        public static QsiTableNode VisitTableRef(TableRefContext context)
        {
            var node = new HanaTableReferenceNode
            {
                Identifier = IdentifierVisitor.VisitTableName(context.tableName()),
                Partition = context.partitionRestriction()?.GetInputText()
            };

            if (context.TryGetRuleContext<ForSystemTimeContext>(out var forSystemTime))
            {
                node.Behavior.SetValue(VisitForSystemTime(forSystemTime));
            }
            else if (context.TryGetRuleContext<ForApplicationTimePeriodContext>(out var forApplicationTimePeriod))
            {
                node.Behavior.SetValue(VisitForApplicationTimePeriod(forApplicationTimePeriod));
            }

            var alias = context.alias();
            var sampling = context.tableSampleClause();

            if (alias == null && sampling == null)
            {
                HanaTree.PutContextSpan(node, context);
                return node;
            }

            HanaTree.PutContextSpan(node, context.Start, alias?.Start ?? sampling?.Start);

            var derivedNode = new HanaDerivedTableNode();
            derivedNode.Columns.SetValue(TreeHelper.CreateAllColumnsDeclaration());
            derivedNode.Source.SetValue(node);

            if (alias != null)
                derivedNode.Alias.SetValue(VisitAlias(alias));

            if (sampling != null)
                derivedNode.Sampling = sampling.GetInputText();

            HanaTree.PutContextSpan(derivedNode, context);

            return derivedNode;
        }

        public static QsiAliasNode VisitAlias(AliasContext context)
        {
            var node = new QsiAliasNode
            {
                Name = IdentifierVisitor.VisitIdentifier(context.name)
            };

            HanaTree.PutContextSpan(node, context);

            return node;
        }

        public static QsiTableNode VisitSubqueryTableExpression(SubqueryTableExpressionContext context)
        {
            var node = VisitSubquery(context.subquery());
            var alias = context.alias() != null ? VisitAlias(context.alias()) : null;

            if (alias != null)
                node.Alias.SetValue(alias);

            return node;
        }

        public static HanaCaseJoinTableNode VisitCaseJoin(CaseJoinContext context)
        {
            var node = new HanaCaseJoinTableNode();
            var elseClause = context.caseJoinElseClause();
            var alias = context.alias();

            node.Source.SetValue(VisitTableRef(context.tableRef()));
            node.WhenSources.AddRange(context.caseJoinWhenClause().Select(VisitCaseJoinWhenClause));

            if (elseClause != null)
                node.ElseSource.SetValue(VisitCaseJoinElseClause(elseClause));

            if (alias != null)
                node.Alias.SetValue(VisitAlias(alias));

            HanaTree.PutContextSpan(node, context);

            return node;
        }

        public static HanaCaseJoinWhenTableNode VisitCaseJoinWhenClause(CaseJoinWhenClauseContext context)
        {
            var node = new HanaCaseJoinWhenTableNode();

            node.Condition.SetValue(ExpressionVisitor.VisitCondition(context.condition()));
            node.Columns.SetValue(VisitColumnListClause(context.columnListClause(), null));
            node.Source.SetValue(VisitTableRef(context.tableRef()));
            node.Predicate.SetValue(ExpressionVisitor.VisitPredicate(context.predicate()));

            HanaTree.PutContextSpan(node, context);

            return node;
        }

        public static HanaCaseJoinElseTableNode VisitCaseJoinElseClause(CaseJoinElseClauseContext context)
        {
            var node = new HanaCaseJoinElseTableNode();

            node.Columns.SetValue(VisitColumnListClause(context.columnListClause(), null));
            node.Source.SetValue(VisitTableRef(context.tableRef()));
            node.Predicate.SetValue(ExpressionVisitor.VisitPredicate(context.predicate()));

            HanaTree.PutContextSpan(node, context);

            return node;
        }

        public static QsiTableNode VisitLateralTableExpression(LateralTableExpressionContext context)
        {
            var subquery = context.subquery();
            var functionExpression = context.functionExpression();

            if (functionExpression != null)
                throw TreeHelper.NotSupportedFeature("Table function");

            QsiTableNode node = new HanaLateralTableNode
            {
                Source =
                {
                    Value = VisitSubquery(subquery)
                }
            };

            if (context.TryGetRuleContext<AliasContext>(out var alias))
                node = TreeHelper.CreateAliasedTableNode(node, IdentifierVisitor.VisitIdentifier(alias.name));

            HanaTree.PutContextSpan(node, context);

            return node;
        }

        public static QsiTableNode VisitCollectionDerivedTable(CollectionDerivedTableContext context)
        {
            throw TreeHelper.NotSupportedFeature("Table function");
        }

        public static QsiTableNode VisitTableFunctionExpression(TableFunctionExpressionContext context)
        {
            throw TreeHelper.NotSupportedFeature("Table function");
        }

        public static QsiTableNode VisitVariableTable(VariableTableContext context)
        {
            throw TreeHelper.NotSupportedFeature("Table variable");
        }

        public static QsiTableNode VisitAssociationTableExpression(AssociationTableExpressionContext context)
        {
            var node = Visit(context);

            if (context.TryGetRuleContext<AliasContext>(out var alias))
                node = TreeHelper.CreateAliasedTableNode(node, IdentifierVisitor.VisitIdentifier(alias.name));

            return node;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static QsiTableNode Visit(AssociationTableExpressionContext context)
            {
                var node = new HanaAssociationTableNode
                {
                    Identifier = IdentifierVisitor.VisitTableName(context.tableName())
                };

                if (context.TryGetRuleContext<ConditionContext>(out var condition))
                    node.Condition.SetValue(ExpressionVisitor.VisitCondition(condition));

                node.Expression.SetValue(ExpressionVisitor.VisitAssociationExpression(context.associationExpression()));

                HanaTree.PutContextSpan(node, context);

                return node;
            }
        }
        #endregion

        public static QsiWhereExpressionNode VisitWhereClause(WhereClauseContext context)
        {
            var node = new QsiWhereExpressionNode();

            node.Expression.SetValue(ExpressionVisitor.VisitCondition(context.condition()));
            HanaTree.PutContextSpan(node, context);

            return node;
        }

        public static QsiGroupingExpressionNode VisitGroupByClause(GroupByClauseContext context)
        {
            var node = new QsiGroupingExpressionNode();
            var groupByExpressionList = context.groupByExpressionList();

            foreach (var child in groupByExpressionList.children.OfType<ParserRuleContext>())
            {
                switch (child)
                {
                    case TableExpressionContext:
                    case GroupingSetContext:
                    {
                        var expressionNode = TreeHelper.Fragment(child.GetInputText());
                        HanaTree.PutContextSpan(expressionNode, child);
                        node.Items.Add(expressionNode);
                        break;
                    }
                }
            }

            if (context.having != null)
                node.Having.SetValue(ExpressionVisitor.VisitCondition(context.having));

            HanaTree.PutContextSpan(node, context);

            return node;
        }

        public static HanaTableBehaviorNode VisitForClause(ForClauseContext context)
        {
            switch (context)
            {
                case ForShareLockClauseContext forShareLock:
                    return VisitForShareLockClause(forShareLock);

                case ForUpdateOfClauseContext forUpdateOf:
                    return VisitForUpdateOfClause(forUpdateOf);

                case ForJsonXmlClauseContext forJsonXml:
                    return VisitForJsonXmlClause(forJsonXml);

                case ForSystemTimeClauseContext forSystemTime:
                    return VisitForSystemTime(forSystemTime.forSystemTime());

                default:
                    throw TreeHelper.NotSupportedTree(context);
            }
        }

        public static HanaTableBehaviorNode VisitForShareLockClause(ForShareLockClauseContext context)
        {
            var node = new HanaTableShareLockBehaviorNode();
            HanaTree.PutContextSpan(node, context);
            return node;
        }

        public static HanaTableBehaviorNode VisitForUpdateOfClause(ForUpdateOfClauseContext context)
        {
            var node = new HanaTableUpdateBehaviorNode
            {
                IgnoreLocked = context.TokenEndsWith(K_IGNORE, K_LOCKED)
            };

            var columnListClause = context.columnListClause();
            var waitNowait = context.waitNowait();

            if (columnListClause != null)
                node.Columns.SetValue(VisitColumnListClause(columnListClause, null));

            if (waitNowait != null)
            {
                if (waitNowait.time == null)
                    node.WaitTime = -1;
                else
                    node.WaitTime = long.Parse(waitNowait.time.Text);
            }

            HanaTree.PutContextSpan(node, context);

            return node;
        }

        public static HanaTableBehaviorNode VisitForJsonXmlClause(ForJsonXmlClauseContext context)
        {
            var json = context.children[1] is ITerminalNode { Symbol: { Type: K_JSON } };

            var node = new HanaTableSerializeBehaviorNode
            {
                Type = json ? HanaTableSerializeType.Json : HanaTableSerializeType.Xml
            };

            var optionList = context.forJsonOrXmlOptionListClause();

            if (optionList != null)
            {
                foreach (var (key, value) in optionList._options.Select(VisitForJsonOrXmlOption))
                    node.Options[key] = value;
            }

            var returns = context.forJsonOrXmlReturnsClause();

            if (returns != null)
                node.ReturnType = returns.GetText()[7..].TrimStart();

            HanaTree.PutContextSpan(node, context);

            return node;
        }

        public static (string Key, string Value) VisitForJsonOrXmlOption(ForJsonOrXmlOptionContext context)
        {
            return new(
                IdentifierUtility.Unescape(context.key.Text),
                IdentifierUtility.Unescape(context.value.Text)
            );
        }

        public static HanaTableBehaviorNode VisitForSystemTime(ForSystemTimeContext context)
        {
            var node = new HanaTableSystemTimeBehaviorNode();

            switch (context)
            {
                case ForSystemTimeAsOfContext forSystemTimeAsOf:
                    node.Time = IdentifierUtility.Unescape(forSystemTimeAsOf.value.Text);
                    break;

                case ForSystemTimeFromContext forSystemTimeFrom:
                    node.FromTo = (
                        IdentifierUtility.Unescape(forSystemTimeFrom.from.Text),
                        IdentifierUtility.Unescape(forSystemTimeFrom.to.Text)
                    );

                    break;

                case ForSystemTimeBetweenContext forSystemTimeBetween:
                    node.Between = (
                        IdentifierUtility.Unescape(forSystemTimeBetween.lower.Text),
                        IdentifierUtility.Unescape(forSystemTimeBetween.upper.Text)
                    );

                    break;
            }

            HanaTree.PutContextSpan(node, context);

            return node;
        }

        public static HanaTableBehaviorNode VisitForApplicationTimePeriod(ForApplicationTimePeriodContext context)
        {
            var node = new HanaTableApplicationTimeBehaviorNode
            {
                Time = IdentifierUtility.Unescape(context.value.Text)
            };

            HanaTree.PutContextSpan(node, context);

            return node;
        }

        // TODO: case expression contains comment
        public static QsiIdentifier GetInferredName(ParserRuleContext context)
        {
            // var text = Regex.Replace(context.GetInputText(), @"\s+", string.Empty);
            // return new QsiIdentifier(text.ToUpper(), false);
            return null;
        }
    }
}
