namespace NodeKit
{
    /// <summary>
    /// 프로토타이핑용 샘플 NodeDefinition 팩토리.
    /// 실제 환경에서는 레지스트리/DB에서 로드한다.
    /// </summary>
    public static class SampleDefinitions
    {
        public static NodeDefinition BwaMem2()
        {
            return new NodeDefinition
            {
                Name = "BWA-MEM2",
                Image = "registry.example.com/bwa-mem2:2.2.1",
                Script = "bwa-mem2 mem $REF $INPUT1 $INPUT2 | samtools sort -o $OUTPUT",
                Ports =
                {
                    new NodePort { Name = "input.fastq.R1", Direction = PortDirection.Input },
                    new NodePort { Name = "input.fastq.R2", Direction = PortDirection.Input },
                    new NodePort { Name = "output.bam",     Direction = PortDirection.Output },
                },
            };
        }

        public static NodeDefinition GatkHc()
        {
            return new NodeDefinition
            {
                Name = "GATK HaplotypeCaller",
                Image = "registry.example.com/gatk:4.4.0",
                Script = "gatk HaplotypeCaller -I $INPUT -O $OUTPUT -R $REF",
                Ports =
                {
                    new NodePort { Name = "input.bam",  Direction = PortDirection.Input },
                    new NodePort { Name = "output.vcf", Direction = PortDirection.Output },
                },
            };
        }

        public static NodeDefinition Trimmomatic()
        {
            return new NodeDefinition
            {
                Name = "Trimmomatic",
                Image = "registry.example.com/trimmomatic:0.39",
                Script = "trimmomatic PE $INPUT1 $INPUT2 $OUTPUT1 /dev/null $OUTPUT2 /dev/null SLIDINGWINDOW:4:20",
                Ports =
                {
                    new NodePort { Name = "input.fastq.R1",  Direction = PortDirection.Input },
                    new NodePort { Name = "input.fastq.R2",  Direction = PortDirection.Input },
                    new NodePort { Name = "output.fastq.R1", Direction = PortDirection.Output },
                    new NodePort { Name = "output.fastq.R2", Direction = PortDirection.Output },
                },
            };
        }
    }
}
