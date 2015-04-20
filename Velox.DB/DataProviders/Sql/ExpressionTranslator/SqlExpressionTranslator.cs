using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Velox.DB.Core;

namespace Velox.DB.Sql
{
    internal sealed class SqlExpressionTranslator
    {
        private class ExpressionMetaData
        {
            public ParameterExpression Iterator;
            public OrmSchema.Relation Relation;
            public OrmSchema Schema;

            public object Key { get { return (object)Relation ?? Schema; } }
        }

        private class SubQuery
        {
            public readonly HashSet<SqlJoinDefinition> Joins = new HashSet<SqlJoinDefinition>();
        }

        private readonly SafeDictionary<Expression, ExpressionMetaData> _metaData = new SafeDictionary<Expression, ExpressionMetaData>();
        private readonly Dictionary<ParameterExpression, Dictionary<object, string>> _relationAliases = new Dictionary<ParameterExpression, Dictionary<object, string>>();
        private readonly QueryParameterCollection _sqlParameters = new QueryParameterCollection();
        private readonly Stack<SubQuery> _subQueries = new Stack<SubQuery>();
        private ParameterExpression _rootIterator;
        private readonly Dictionary<ParameterExpression, ParameterExpression> _rootIterators = new Dictionary<ParameterExpression, ParameterExpression>();
        private readonly SqlDialect _sqlDialect;
        private readonly string _tableAlias;
        private readonly OrmSchema _schema;

        public SqlExpressionTranslator(SqlDialect sqlDialect, OrmSchema schema, string tableAlias)
        {
            _schema = schema;
            _tableAlias = tableAlias;
            _sqlDialect = sqlDialect;

            _subQueries.Push(new SubQuery());
        }

        private ParameterExpression GetRootIterator(ParameterExpression iterator) // TODO: needs better name
        {
            return _rootIterators.ContainsKey(iterator) ? _rootIterators[iterator] : iterator;
        }

        private SubQuery CurrentQuery
        {
            get { return _subQueries.Peek(); }
        }

        public QueryParameterCollection SqlParameters
        {
            get { return _sqlParameters; }
        }

        public HashSet<SqlJoinDefinition> Joins
        {
            get { return CurrentQuery.Joins; }
        }


        private void PrepareForNewExpression(ParameterExpression rootIterator, string tableAlias, OrmSchema schema)
        {
            if (_rootIterator == null)
            {
                _rootIterator = rootIterator;

                _relationAliases[rootIterator] = new Dictionary<object, string> { { schema, tableAlias } };
            }

            _metaData[rootIterator] = new ExpressionMetaData
            {
                Relation = null,
                Iterator = rootIterator,
                Schema = schema
            };

            _rootIterators[rootIterator] = _rootIterator; // make additional root iterators equivalent to first one
        }

        private OrmSchema.Relation GetRelation(Expression expression, string memberName)
        {
            return _metaData[expression].Schema.Relations[memberName];
        }

        private string ProcessRelation(Expression fullExpression, Expression leftExpression, string memberName)
        {
            var parentMetaData = _metaData[leftExpression];

            if (parentMetaData != null)
            {
                if (_metaData.ContainsKey(fullExpression))
                    return null; // relation already visited

                var iterator = GetRootIterator(parentMetaData.Iterator);

                var relation = parentMetaData.Schema.Relations[memberName];
                var leftAlias = _relationAliases[iterator][parentMetaData.Key];

                if (relation != null && relation.RelationType == OrmSchema.RelationType.ManyToOne)
                {
                    if (!_relationAliases[iterator].ContainsKey(relation))
                    {
                        var sqlJoin = new SqlJoinDefinition
                        {
                            Left = new SqlJoinPart(parentMetaData.Schema, relation.LocalField, leftAlias),
                            Right = new SqlJoinPart(relation.ForeignSchema, relation.ForeignField, SqlNameGenerator.NextTableAlias()),
                            Type = SqlJoinType.Inner
                        };

                        CurrentQuery.Joins.Add(sqlJoin);

                        _relationAliases[iterator][relation] = sqlJoin.Right.Alias;
                    }

                    _metaData[fullExpression] = new ExpressionMetaData { Iterator = iterator, Relation = relation, Schema = relation.ForeignSchema };
                }
                else if (relation == null)
                {
                    return leftAlias + "." + memberName;
                }
            }

            return null;
        }


        public string Translate(QueryExpression queryExpression)
        {
            var lambda = ((LambdaQueryExpression) queryExpression).Expression;

            PrepareForNewExpression(lambda.Parameters[0], _tableAlias, _schema);

            try
            {
                return Translate(lambda);
            }
            catch (SqlExpressionTranslatorException)
            {
                return null; // we couldn't translate the given expression
            }
        }

        private string Translate(Expression expression)
        {
            if (expression == null)
                return null;

            expression = PartialEvaluator.Eval(expression);

            if (expression is BinaryExpression)
                return TranslateBinary((BinaryExpression) expression);

            if (expression is MemberExpression)
                return TranslateMember((MemberExpression) expression);

            if (expression is MethodCallExpression)
                return TranslateMethodCall((MethodCallExpression) expression);

            if (expression is UnaryExpression)
                return TranslateUnary((UnaryExpression) expression);

            if (expression is ConstantExpression)
                return TranslateConstant((ConstantExpression) expression);

            if (expression is ParameterExpression)
                return null;

            if (expression is LambdaExpression)
                return Translate(((LambdaExpression)expression).Body);

            throw new SqlExpressionTranslatorException(expression.ToString());
        }

        private string TranslateConstant(ConstantExpression node)
        {
            if (node.Type.Inspector().Is(TypeFlags.Numeric | TypeFlags.Boolean | TypeFlags.String | TypeFlags.DateTime | TypeFlags.Enum))
            {
                return CreateParameter(node.Value);
            }

            return null;
        }

        private string TranslateUnary(UnaryExpression expression)
        {
            switch (expression.NodeType)
            {
                case ExpressionType.Convert:
                case ExpressionType.Quote:
                    return Translate(expression.Operand);

                case ExpressionType.Not:
                    return "(NOT " + Translate(expression.Operand) + ")";
            }

            throw new SqlExpressionTranslatorException(expression.ToString());
        }

        private string TranslateMethodCall(MethodCallExpression node)
        {
            var methodName = node.Method.Name;
            MemberExpression leftExpression = null;
            var arguments = new List<LambdaExpression>();

            if (node.Method.DeclaringType == typeof(string))
            {
                string arg = Translate(node.Object);
                
                object[] stringArguments = node.Arguments.Select(UnQuote).OfType<ConstantExpression>().Select(exp => exp.Value).ToArray();

                switch (methodName)
                {
                    case "StartsWith":
                        return string.Format("({0} like {1})", arg, CreateParameter(stringArguments[0] + "%"));
                    case "EndsWith":
                        return string.Format("({0} like {1})", arg, CreateParameter("%" + stringArguments[0]));
                    case "Trim":
                        return string.Format(_sqlDialect.SqlFunctionName(SqlDialect.Function.Trim), stringArguments[0]);
                }

                throw new SqlExpressionTranslatorException(node.ToString());
            }

            if (node.Method.DeclaringType.IsConstructedGenericType && node.Method.DeclaringType.GetGenericTypeDefinition() == typeof(IDataSet<>))
            {
                leftExpression = node.Object as MemberExpression;
                arguments.AddRange(node.Arguments.Select(UnQuote).OfType<LambdaExpression>());
            }

            if (node.Method.DeclaringType == typeof(Enumerable) && node.Arguments.Count > 0)
            {
                leftExpression = node.Arguments[0] as MemberExpression;
                arguments.AddRange(node.Arguments.Skip(1).Select(UnQuote).OfType<LambdaExpression>());
            }

            if (leftExpression != null)
            {
                switch (methodName)
                {
                    case "Any":
                    case "Count":
                    case "All":
                        return TranslateOneToMany(
                            methodName,
                            leftExpression,
                            null,
                            arguments.Count > 0 ? arguments[0] : null
                            );
                    case "Sum":
                    case "Avg":
                        return TranslateOneToMany(
                            methodName,
                            leftExpression,
                            arguments[0],
                            null
                            );
                }
            }

            throw new SqlExpressionTranslatorException(node.ToString());
        }

        private string TranslateMember(MemberExpression node)
        {
            if (node.Expression.Type == typeof(string) && node.Member.Name == "Length")
            {
                string fnName = _sqlDialect.SqlFunctionName(SqlDialect.Function.StringLength, Translate(node.Expression));

                if (fnName != null)
                    return fnName;
            }

            if (Translate(node.Expression) != null)
                throw new SqlExpressionTranslatorException(node.ToString());

            string sql = ProcessRelation(node, node.Expression, node.Member.Name);

            if (sql != null)
                return _sqlDialect.QuoteField(sql);

            return null;
        }

        private string TranslateBinary(BinaryExpression expression)
        {
            if (expression.Right.NodeType == ExpressionType.Constant && (((ConstantExpression)expression.Right).Value == null))
            {
                switch (expression.NodeType)
                {
                    case ExpressionType.Equal:
                        return "(" + Translate(expression.Left) + " IS NULL)";
                    case ExpressionType.NotEqual:
                        return "(" + Translate(expression.Left) + " IS NOT NULL)";
                    default:
                        throw new SqlExpressionTranslatorException(expression.ToString());
                }
            }

            string op;

            switch (expression.NodeType)
            {
                case ExpressionType.Add:
                case ExpressionType.AddChecked:
                    op = "+";
                    break;
                case ExpressionType.AndAlso:
                    op = "AND";
                    break;
                case ExpressionType.Divide:
                    op = "/";
                    break;
                case ExpressionType.Equal:
                    op = "=";
                    break;
                case ExpressionType.GreaterThan:
                    op = ">";
                    break;
                case ExpressionType.GreaterThanOrEqual:
                    op = ">=";
                    break;
                case ExpressionType.LessThan:
                    op = "<";
                    break;
                case ExpressionType.LessThanOrEqual:
                    op = "<=";
                    break;
                case ExpressionType.Multiply:
                case ExpressionType.MultiplyChecked:
                    op = "*";
                    break;
                case ExpressionType.NotEqual:
                    op = "<>";
                    break;
                case ExpressionType.OrElse:
                    op = "OR";
                    break;
                case ExpressionType.Subtract:
                case ExpressionType.SubtractChecked:
                    op = "-";
                    break;
                default:
                    throw new SqlExpressionTranslatorException(expression.ToString());
            }

            return string.Format("({0} {1} {2})", Translate(expression.Left), op, Translate(expression.Right));
        }

        private static Expression UnQuote(Expression expression)
        {
            return expression.NodeType == ExpressionType.Quote ? ((UnaryExpression)expression).Operand : expression;
        }

        private string TranslateOneToMany(string functionName, MemberExpression memberExpression, LambdaExpression fieldExpression, LambdaExpression filterExpression)
        {
            if (!string.IsNullOrEmpty(TranslateMember(memberExpression))) // this shouldn't return any SQL
                throw new SqlExpressionTranslatorException(memberExpression.ToString());

            var relation = GetRelation(memberExpression.Expression, memberExpression.Member.Name);

            if (relation == null)
                throw new SqlExpressionTranslatorException(memberExpression.ToString());

            if (fieldExpression != null && filterExpression != null && fieldExpression.Parameters[0] != filterExpression.Parameters[0])
                throw new SqlExpressionTranslatorException(null);

            var iterator = fieldExpression != null ? fieldExpression.Parameters[0] : filterExpression != null ? filterExpression.Parameters[0] : Expression.Parameter(relation.ElementType);

            LambdaExpression relationExpression = CreateToManyFilterExpression(relation, memberExpression.Expression, filterExpression, iterator);

            var template = _toManyTemplates[functionName];

            if (template == null)
                throw new NotSupportedException(functionName);

            var alias = SqlNameGenerator.NextTableAlias();

            _metaData[iterator] = new ExpressionMetaData { Iterator = iterator, Relation = relation, Schema = relation.ForeignSchema };

            _relationAliases[iterator] = new Dictionary<object, string> { { relation, alias } };

            _subQueries.Push(new SubQuery());

            string sqlFields = Translate(fieldExpression);
            string sqlWhere = Translate(relationExpression);
            string sqlJoins = (Joins.Count > 0) ? string.Join(" ", Joins.Select(join => join.ToSql(_sqlDialect))) : null;

            _subQueries.Pop();

            return string.Format(template,
                                    sqlFields,
                                    _sqlDialect.QuoteTable(relation.ForeignSchema.MappedName) + " " + alias, sqlJoins ?? "",
                                    sqlWhere
                                );
        }

        private static LambdaExpression CreateToManyFilterExpression(OrmSchema.Relation relation, Expression localExpression, LambdaExpression filterLambda, ParameterExpression lambdaParameter)
        {
            var expression = Expression.Equal(
                Expression.MakeMemberAccess(localExpression, relation.LocalField.Accessor.AsMember),
                Expression.MakeMemberAccess(lambdaParameter, relation.ForeignField.Accessor.AsMember)
                );

            if (filterLambda != null)
                expression = Expression.AndAlso(filterLambda.Body, expression);

            return Expression.Lambda(expression, lambdaParameter);
        }
        
        private string CreateParameter(object value)
        {
            var parameterName = SqlNameGenerator.NextParameterName();

            SqlParameters[parameterName] = value;

            return _sqlDialect.CreateParameterExpression(parameterName);
        }

        private static readonly SafeDictionary<string, string> _toManyTemplates = new SafeDictionary<string, string>()
        {
            {"Any", "exists (select * from {1} {2} where {3})"},
            {"Count", "(select count(*) from {1} {2} where {3})"},
            {"Sum","(select sum({0}) from {1} {2} where {3})"},
            {"Average","(select avg({0}) from {1} {2} where {3})"}
        };

    }
}