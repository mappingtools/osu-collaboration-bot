﻿using System;
using CollaborationBot.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;

#nullable disable

namespace CollaborationBot.Entities
{
    public partial class OsuCollabContext : DbContext {
        private readonly AppSettings appSettings;

        public OsuCollabContext()
        {
        }

        public OsuCollabContext(DbContextOptions<OsuCollabContext> options, AppSettings appSettings)
            : base(options) {
            this.appSettings = appSettings;

            NpgsqlConnection.GlobalTypeMapper.MapEnum<PartStatus>("part_status");
            NpgsqlConnection.GlobalTypeMapper.MapEnum<ProjectStatus>("project_status");
            NpgsqlConnection.GlobalTypeMapper.MapEnum<ProjectRole>("project_role");
        }

        public virtual DbSet<Assignment> Assignments { get; set; }
        public virtual DbSet<AutoUpdate> AutoUpdates { get; set; }
        public virtual DbSet<Guild> Guilds { get; set; }
        public virtual DbSet<Member> Members { get; set; }
        public virtual DbSet<Part> Parts { get; set; }
        public virtual DbSet<Project> Projects { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseNpgsql(appSettings.ConnectionString);
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasPostgresEnum(null, "part_status", new[] { "not_finished", "finished", "in_progress", "in_review", "abandoned", "locked" })
                .HasPostgresEnum(null, "project_role", new[] { "owner", "manager", "member" })
                .HasPostgresEnum(null, "project_status", new[] { "finished", "in_review", "in_progress", "assigning_parts", "searching_for_members", "on_hold", "not_started" })
                .HasAnnotation("Relational:Collation", "English_Netherlands.1252");

            modelBuilder.Entity<Assignment>(entity =>
            {
                entity.ToTable("assignments");

                entity.Property(e => e.Id).HasColumnName("id");

                entity.Property(e => e.Deadline).HasColumnName("deadline");

                entity.Property(e => e.MemberId).HasColumnName("member_id");

                entity.Property(e => e.PartId).HasColumnName("part_id");

                entity.HasOne(d => d.Member)
                    .WithMany(p => p.Assignments)
                    .HasForeignKey(d => d.MemberId)
                    .HasConstraintName("assignments_member_id_fkey");

                entity.HasOne(d => d.Part)
                    .WithMany(p => p.Assignments)
                    .HasForeignKey(d => d.PartId)
                    .HasConstraintName("assignments_part_id_fkey");
            });

            modelBuilder.Entity<AutoUpdate>(entity =>
            {
                entity.ToTable("auto_updates");

                entity.Property(e => e.Id)
                    .HasColumnName("id")
                    .HasDefaultValueSql("nextval('\"autoUpdates_id_seq\"'::regclass)");

                entity.Property(e => e.Cooldown).HasColumnName("cooldown");

                entity.Property(e => e.DoPing).HasColumnName("do_ping");

                entity.Property(e => e.ProjectId).HasColumnName("project_id");

                entity.Property(e => e.ShowOsu).HasColumnName("show_osu");

                entity.Property(e => e.ShowOsz).HasColumnName("show_osz");

                entity.Property(e => e.UniqueChannelId).HasColumnName("unique_channel_id");

                entity.HasOne(d => d.Project)
                    .WithMany(p => p.AutoUpdates)
                    .HasForeignKey(d => d.ProjectId)
                    .HasConstraintName("autoUpdates_project_id_fkey");
            });

            modelBuilder.Entity<Guild>(entity =>
            {
                entity.ToTable("guilds");

                entity.Property(e => e.Id).HasColumnName("id");

                entity.Property(e => e.UniqueGuildId).HasColumnName("unique_guild_id");
            });

            modelBuilder.Entity<Member>(entity =>
            {
                entity.ToTable("members");

                entity.Property(e => e.Id).HasColumnName("id");

                entity.Property(e => e.Priority).HasColumnName("priority");

                entity.Property(e => e.ProjectId).HasColumnName("project_id");

                entity.Property(e => e.UniqueMemberId).HasColumnName("unique_member_id");

                entity.HasOne(d => d.Project)
                    .WithMany(p => p.Members)
                    .HasForeignKey(d => d.ProjectId)
                    .HasConstraintName("members_project_id_fkey");
            });

            modelBuilder.Entity<Part>(entity =>
            {
                entity.ToTable("parts");

                entity.Property(e => e.Id).HasColumnName("id");

                entity.Property(e => e.End).HasColumnName("end");

                entity.Property(e => e.Name)
                    .HasMaxLength(255)
                    .HasColumnName("name");

                entity.Property(e => e.ProjectId).HasColumnName("project_id");

                entity.Property(e => e.Start).HasColumnName("start");

                entity.HasOne(d => d.Project)
                    .WithMany(p => p.Parts)
                    .HasForeignKey(d => d.ProjectId)
                    .HasConstraintName("parts_project_id_fkey");
            });

            modelBuilder.Entity<Project>(entity =>
            {
                entity.ToTable("projects");

                entity.Property(e => e.Id).HasColumnName("id");

                entity.Property(e => e.Description)
                    .HasMaxLength(255)
                    .HasColumnName("description");

                entity.Property(e => e.GuildId).HasColumnName("guild_id");

                entity.Property(e => e.MaxAssignments).HasColumnName("max_assignments");

                entity.Property(e => e.Name)
                    .HasMaxLength(255)
                    .HasColumnName("name");

                entity.Property(e => e.PartRestrictedUpload).HasColumnName("part_restricted_upload");

                entity.Property(e => e.PriorityPicking).HasColumnName("priority_picking");

                entity.Property(e => e.SelfAssignmentAllowed).HasColumnName("self_assignment_allowed");

                entity.Property(e => e.UniqueRoleId).HasColumnName("unique_role_id");

                entity.HasOne(d => d.Guild)
                    .WithMany(p => p.Projects)
                    .HasForeignKey(d => d.GuildId)
                    .HasConstraintName("projects_guild_id_fkey");
            });

            OnModelCreatingPartial(modelBuilder);
        }

        partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
    }
}
