using System;
using System.Data;
using FluentMigrator;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Test.Framework
{
    [Category("DbMigrationTest")]
    [Category("DbTest")]
    public abstract class MigrationTest<TMigration> : DbTest
        where TMigration : NzbDroneMigrationBase
    {
        protected long MigrationVersion => ((MigrationAttribute)Attribute.GetCustomAttribute(typeof(TMigration), typeof(MigrationAttribute))).Version;

        [SetUp]
        public override void SetupDb()
        {
            SetupContainer();
        }

        protected virtual IDirectDataMapper WithMigrationTestDb(Action<TMigration> beforeMigration = null)
        {
            return WithMigrationAction(beforeMigration).GetDirectDataMapper();
        }

        protected virtual IDbConnection WithDapperMigrationTestDb(Action<TMigration> beforeMigration = null)
        {
            return WithMigrationAction(beforeMigration).OpenConnection();
        }

        protected override void SetupLogging()
        {
            // Construct the provider directly rather than via Mocker.Resolve: auto-wiring picks
            // NLogLoggerProvider's greediest constructor, which pulls in NLog's LogFactory and in turn
            // its internal ILoggingConfigurationLoader - an internal interface in a strong-named
            // assembly that Castle/Moq cannot proxy. The parameterless constructor binds to
            // LogManager.LogFactory, which is what these tests want anyway.
            Mocker.SetConstant<ILoggerProvider>(new NLogLoggerProvider());
        }

        private ITestDatabase WithMigrationAction(Action<TMigration> beforeMigration = null)
        {
            return WithTestDb(new MigrationContext(MigrationType, MigrationVersion)
            {
                BeforeMigration = m =>
                {
                    if (beforeMigration != null && m is TMigration migration)
                    {
                        beforeMigration(migration);
                    }
                }
            });
        }
    }
}
