﻿using EntityFrameworkCore.Generator.Metadata.Generation;
using EntityFrameworkCore.Generator.Options;
using EntityFrameworkCore.Generator.Templates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;

namespace EntityFrameworkCore.Generator
{
    public class CodeGenerator : ICodeGenerator
    {
        private readonly ILogger _logger;
        private readonly IDiagnosticsLogger<DbLoggerCategory.Scaffolding> _diagnosticsLogger;
        private readonly ModelGenerator _modelGenerator;

        public CodeGenerator(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<CodeGenerator>();
            _diagnosticsLogger = new DiagnosticsLogger<DbLoggerCategory.Scaffolding>(loggerFactory, new LoggingOptions(), new DiagnosticListener(""));
            _modelGenerator = new ModelGenerator(loggerFactory);
        }

        public GeneratorOptions Options { get; set; }

        public bool Generate(GeneratorOptions options)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));

            var factory = GetDatabaseModelFactory();
            var databaseModel = GetDatabaseModel(factory);

            if (databaseModel == null)
                throw new InvalidOperationException("Failed to create database model");

            // update database variables
            Options.Database.Name = databaseModel.DatabaseName;

            _logger.LogTrace($"Loaded database model for: {databaseModel.DatabaseName}");

            var context = _modelGenerator.Generate(Options, databaseModel);
            GenerateFiles(context);

            return true;
        }

        private void GenerateFiles(EntityContext entityContext)
        {
            GenerateDataContext(entityContext);
            GenerateEntityClasses(entityContext);
            GenerateMappingClasses(entityContext);

            if (Options.Data.Query.Generate)
                GenerateQueryExtensions(entityContext);

            GenerateModelClasses(entityContext);
        }

        private void GenerateQueryExtensions(EntityContext entityContext)
        {
            foreach (var entity in entityContext.Entities)
            {
                var directory = Options.Data.Query.Directory;
                var file = entity.EntityClass + "Extensions.cs";
                var path = Path.Combine(directory, file);

                var template = new QueryExtensionTemplate(entity, Options);
                template.WriteCode(path);
            }

        }

        private void GenerateMappingClasses(EntityContext entityContext)
        {
            foreach (var entity in entityContext.Entities)
            {
                var directory = Options.Data.Mapping.Directory;
                var file = entity.MappingClass + ".cs";
                var path = Path.Combine(directory, file);

                var template = new MappingClassTemplate(entity);
                template.WriteCode(path);
            }
        }

        private void GenerateEntityClasses(EntityContext entityContext)
        {
            foreach (var entity in entityContext.Entities)
            {
                Options.Variables.Set("Entity.Name", entity.EntityClass);

                var directory = Options.Data.Entity.Directory;
                var file = entity.EntityClass + ".cs";
                var path = Path.Combine(directory, file);

                var template = new EntityClassTemplate(entity);
                template.WriteCode(path);
            }

            Options.Variables.Remove("Entity.Name");
        }

        private void GenerateDataContext(EntityContext entityContext)
        {
            var directory = Options.Data.Context.Directory;
            var file = entityContext.ContextClass + ".cs";
            var path = Path.Combine(directory, file);

            var template = new DataContextTemplate(entityContext);
            template.WriteCode(path);
        }


        private void GenerateModelClasses(EntityContext entityContext)
        {
            foreach (var entity in entityContext.Entities)
            {
                Options.Variables.Set("Entity.Name", entity.EntityClass);
                if (entity.Models.Count <= 0)
                    continue;

                GenerateModelClasses(entity);
                GenerateValidatorClasses(entity);
                GenerateMapperClass(entity);
            }

            Options.Variables.Remove("Entity.Name");
        }


        private void GenerateModelClasses(Entity entity)
        {
            foreach (var model in entity.Models)
            {
                Options.Variables.Set("Model.Name", entity.EntityClass);

                var directory = GetModelDirectory(model);
                var file = model.ModelClass + ".cs";
                var path = Path.Combine(directory, file);

                var template = new ModelClassTemplate(model);
                template.WriteCode(path);
            }

            Options.Variables.Remove("Model.Name");
        }

        private string GetModelDirectory(Model model)
        {
            if (model.ModelType == ModelType.Create)
                return Options.Model.Create.Directory;

            if (model.ModelType == ModelType.Update)
                return Options.Model.Update.Directory;

            return Options.Model.Read.Directory;
        }


        private void GenerateValidatorClasses(Entity entity)
        {
            foreach (var model in entity.Models)
            {
                Options.Variables.Set("Model.Name", entity.EntityClass);

                // don't validate read models
                if (model.ModelType == ModelType.Read)
                    continue;

                var directory = Options.Model.Validator.Directory;
                var file = model.ValidatorClass + ".cs";
                var path = Path.Combine(directory, file);

                var template = new ValidatorClassTemplate(model);
                template.WriteCode(path);
            }

            Options.Variables.Remove("Model.Name");
        }


        private void GenerateMapperClass(Entity entity)
        {
            var directory = Options.Model.Mapper.Directory;
            var file = entity.MapperClass + ".cs";
            var path = Path.Combine(directory, file);

            var template = new MapperClassTemplate(entity);
            template.WriteCode(path);
        }


        private DatabaseModel GetDatabaseModel(IDatabaseModelFactory factory)
        {
            _logger.LogTrace("Creating database model ...");

            var database = Options.Database;
            return factory.Create(database.ConnectionString, database.Tables, database.Schemas);
        }

        private IDatabaseModelFactory GetDatabaseModelFactory()
        {
            var provider = Options.Database.Provider;

            _logger.LogTrace($"Creating database model factory for: {provider}");
            if (provider == DatabaseProviders.SqlServer)
                return new Microsoft.EntityFrameworkCore.SqlServer.Scaffolding.Internal.SqlServerDatabaseModelFactory(_diagnosticsLogger);

            if (provider == DatabaseProviders.PostgreSQL)
                return new Npgsql.EntityFrameworkCore.PostgreSQL.Scaffolding.Internal.NpgsqlDatabaseModelFactory(_diagnosticsLogger);

            //if (Options.Database.Provider == DatabaseProviders.Sqlite)
            //    return new Microsoft.EntityFrameworkCore.Sqlite.Scaffolding.Internal.SqliteDatabaseModelFactory(_diagnosticsLogger, null);

            throw new NotSupportedException($"The specified provider '{provider}' is not supported.");
        }
    }
}
