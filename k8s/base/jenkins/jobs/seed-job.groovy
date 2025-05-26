job('seed-job') {
    description('Seed job to create/update all Job DSL jobs in the jobs/ directory')
    steps {
        dsl {
            external('jobs/timestamp-job.groovy')
            removeAction('DELETE')
        }
    }
} 