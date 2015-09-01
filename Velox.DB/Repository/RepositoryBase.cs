﻿#region License
//=============================================================================
// Velox.DB - Portable .NET ORM 
//
// Copyright (c) 2015 Philippe Leybaert
//
// Permission is hereby granted, free of charge, to any person obtaining a copy 
// of this software and associated documentation files (the "Software"), to deal 
// in the Software without restriction, including without limitation the rights 
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell 
// copies of the Software, and to permit persons to whom the Software is 
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR 
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
// IN THE SOFTWARE.
//=============================================================================
#endregion

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Velox.DB.Core;
using Velox.DB.Sql;

namespace Velox.DB
{
    internal abstract class Repository
    {
        internal Vx.Context Context { get; }
        internal OrmSchema Schema { get; }

        internal IDataProvider DataProvider => Context.DataProvider;

        protected Repository(Type type, Vx.Context context)
        {
            Context = context;

            Schema = new OrmSchema(type, this);
        }

        protected internal IEnumerable<object> GetRelationObjects(QuerySpec filter, OrmSchema.Relation parentRelation, object parentObject)
        {
            var objects = from o in DataProvider.GetObjects(filter.Native,Schema)
                          select Vx.WithLoadedRelations(Schema.UpdateObject(Activator.CreateInstance(Schema.ObjectType), o),Schema.DatasetRelations) ;

            if (parentRelation?.ReverseRelation != null)
                objects = from o in objects select parentRelation.ReverseRelation.SetField(o, parentObject);

            if (filter.Code != null)
                objects = from o in objects where filter.Code.IsFilterMatch(o) select o;

            return objects;
        }

        protected bool Save(object obj, bool saveRelations = false, bool? create = null)
        {
            if (create == null)
                create = Schema.IncrementKeys.Length > 0 && Equals(Schema.IncrementKeys[0].GetField(obj), Schema.IncrementKeys[0].FieldInfo.TypeInspector.DefaultValue());

            bool cancelSave = false;

            if (create.Value)
                Fire_ObjectCreating(obj, ref cancelSave);
            else
                Fire_ObjectSaving(obj, ref cancelSave);

            if (cancelSave)
                return false;

            var toOneRelations = Schema.Relations.Values.Where(r => r.IsToOne && !r.ReadOnly);
            var toManyRelations = Schema.Relations.Values.Where(r => r.RelationType == OrmSchema.RelationType.OneToMany && !r.ReadOnly);

            // Update and save ManyToOne relations
            foreach (var relation in toOneRelations)
            {
                var foreignObject = relation.GetField(obj);

                if (foreignObject == null)
                    continue;

                if (saveRelations)
                    relation.ForeignSchema.Repository.Save(foreignObject, saveRelations, create);

                var foreignKeyValue = relation.ForeignField.GetField(foreignObject);

                if (!Equals(relation.LocalField.GetField(obj), foreignKeyValue))
                    relation.LocalField.SetField(obj, foreignKeyValue);
            }

            var serializedEntity = Schema.SerializeObject(obj);

            var result = DataProvider.WriteObject(serializedEntity, create.Value, Schema);

            if (result.OriginalUpdated)
                Schema.UpdateObject(obj, serializedEntity);

            // Update and save OneToMany relations
            foreach (var relation in toManyRelations)
            {
                var foreignCollection = (IEnumerable) relation.GetField(obj);

                if (foreignCollection == null)
                    continue;

                foreach (var foreignObject in foreignCollection)
                {
                    var foreignKeyValue = relation.ForeignField.GetField(foreignObject);
                    var localKeyValue = relation.LocalField.GetField(obj);

                    if (!Equals(localKeyValue, foreignKeyValue))
                        relation.ForeignField.SetField(foreignObject, localKeyValue);

                    if (saveRelations)
                        relation.ForeignSchema.Repository.Save(foreignObject, saveRelations, create);
                }
            }

            if (result.Success)
            {
                if (create.Value)
                    Fire_ObjectCreated(obj);
                else
                    Fire_ObjectSaved(obj);
            }

            return result.Success;
        }

        internal void Purge()
        {
            DataProvider.Purge(Schema);
        }

        internal QuerySpec CreateQuerySpec(FilterSpec filter, ScalarSpec scalarSpec = null, int? skip = null, int? take = null, SortOrderSpec sortSpec = null)
        {
            if (DataProvider.SupportsQueryTranslation())
                return DataProvider.CreateQuerySpec(filter, scalarSpec, sortSpec, skip, take, Schema);

            var querySpec = new QuerySpec(new CodeQuerySpec(), null);

            var codeQuerySpec = (CodeQuerySpec) querySpec.Code;

            if (filter != null)
                foreach (var expression in filter.Expressions)
                    codeQuerySpec.AddFilter(Schema, expression);

            if (sortSpec != null)
                foreach (var expression in sortSpec.Expressions)
                    codeQuerySpec.AddSort(Schema, expression.Expression, expression.SortOrder);

            if (scalarSpec != null)
                codeQuerySpec.AddScalar(Schema, scalarSpec.Expression);

            codeQuerySpec.Skip = skip;
            codeQuerySpec.Take = take;

            return querySpec;
        }

        public abstract void Fire_ObjectCreating(object obj, ref bool cancel);
        public abstract void Fire_ObjectCreated(object obj);
        public abstract void Fire_ObjectSaving(object obj, ref bool cancel);
        public abstract void Fire_ObjectSaved(object obj);
        public abstract void Fire_ObjectDeleting(object obj, ref bool cancel);
        public abstract void Fire_ObjectDeleted(object obj);
    }




}