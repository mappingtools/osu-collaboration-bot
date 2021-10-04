-- SCHEMA: public

-- DROP SCHEMA public ;

CREATE SCHEMA public
    AUTHORIZATION postgres;

COMMENT ON SCHEMA public
    IS 'standard public schema';

GRANT ALL ON SCHEMA public TO PUBLIC;

GRANT ALL ON SCHEMA public TO postgres;

-- Type: part_status

-- DROP TYPE public.part_status;

CREATE TYPE public.part_status AS ENUM
    ('not_finished', 'finished', 'in_progress', 'in_review', 'abandoned', 'locked');

ALTER TYPE public.part_status
    OWNER TO postgres;

-- Type: project_role

-- DROP TYPE public.project_role;

CREATE TYPE public.project_role AS ENUM
    ('owner', 'manager', 'member');

ALTER TYPE public.project_role
    OWNER TO postgres;

-- Type: project_status

-- DROP TYPE public.project_status;

CREATE TYPE public.project_status AS ENUM
    ('finished', 'in_review', 'in_progress', 'assigning_parts', 'searching_for_members', 'on_hold', 'not_started');

ALTER TYPE public.project_status
    OWNER TO postgres;

-- Table: public.assignments

-- DROP TABLE public.assignments;

CREATE TABLE public.assignments
(
    id integer NOT NULL DEFAULT nextval('assignments_id_seq'::regclass),
    member_id integer NOT NULL,
    part_id integer NOT NULL,
    deadline timestamp without time zone,
    last_reminder timestamp without time zone,
    CONSTRAINT assignments_pkey PRIMARY KEY (id),
    CONSTRAINT "Unique member + part" UNIQUE (member_id, part_id),
    CONSTRAINT assignments_member_id_fkey FOREIGN KEY (member_id)
        REFERENCES public.members (id) MATCH SIMPLE
        ON UPDATE NO ACTION
        ON DELETE CASCADE,
    CONSTRAINT assignments_part_id_fkey FOREIGN KEY (part_id)
        REFERENCES public.parts (id) MATCH SIMPLE
        ON UPDATE NO ACTION
        ON DELETE CASCADE
)

TABLESPACE pg_default;

ALTER TABLE public.assignments
    OWNER to postgres;

-- Table: public.auto_updates

-- DROP TABLE public.auto_updates;

CREATE TABLE public.auto_updates
(
    id integer NOT NULL DEFAULT nextval('"autoUpdates_id_seq"'::regclass),
    project_id integer NOT NULL,
    unique_channel_id numeric NOT NULL,
    cooldown interval,
    do_ping boolean NOT NULL DEFAULT false,
    show_osu boolean NOT NULL DEFAULT true,
    show_osz boolean NOT NULL DEFAULT false,
    last_time timestamp without time zone,
    CONSTRAINT "autoUpdates_pkey" PRIMARY KEY (id),
    CONSTRAINT "autoUpdates_project_id_fkey" FOREIGN KEY (project_id)
        REFERENCES public.projects (id) MATCH SIMPLE
        ON UPDATE NO ACTION
        ON DELETE CASCADE
)

TABLESPACE pg_default;

ALTER TABLE public.auto_updates
    OWNER to postgres;

-- Table: public.guilds

-- DROP TABLE public.guilds;

CREATE TABLE public.guilds
(
    id integer NOT NULL DEFAULT nextval('guilds_id_seq'::regclass),
    unique_guild_id numeric NOT NULL,
    collab_category_id numeric,
    max_collabs_per_person integer NOT NULL DEFAULT 1,
    CONSTRAINT guilds_pkey PRIMARY KEY (id),
    CONSTRAINT guilds_unique_id UNIQUE (unique_guild_id)
)

TABLESPACE pg_default;

ALTER TABLE public.guilds
    OWNER to postgres;

-- Table: public.members

-- DROP TABLE public.members;

CREATE TABLE public.members
(
    id integer NOT NULL DEFAULT nextval('members_id_seq'::regclass),
    project_id integer NOT NULL,
    unique_member_id numeric NOT NULL,
    project_role project_role NOT NULL DEFAULT 'member'::project_role,
    priority integer,
    alias character varying(255) COLLATE pg_catalog."default",
    tags character varying(255) COLLATE pg_catalog."default",
    CONSTRAINT members_pkey PRIMARY KEY (id),
    CONSTRAINT "Single membership" UNIQUE (project_id, unique_member_id),
    CONSTRAINT members_project_id_fkey FOREIGN KEY (project_id)
        REFERENCES public.projects (id) MATCH SIMPLE
        ON UPDATE NO ACTION
        ON DELETE CASCADE
)

TABLESPACE pg_default;

ALTER TABLE public.members
    OWNER to postgres;

COMMENT ON CONSTRAINT "Single membership" ON public.members
    IS 'A user may only be registered to a project once per project.';

-- Table: public.parts

-- DROP TABLE public.parts;

CREATE TABLE public.parts
(
    id integer NOT NULL DEFAULT nextval('parts_id_seq'::regclass),
    project_id integer NOT NULL,
    name character varying(255) COLLATE pg_catalog."default",
    status part_status NOT NULL DEFAULT 'not_finished'::part_status,
    start integer,
    "end" integer,
    CONSTRAINT parts_pkey PRIMARY KEY (id),
    CONSTRAINT "unique name" UNIQUE (project_id, name),
    CONSTRAINT parts_project_id_fkey FOREIGN KEY (project_id)
        REFERENCES public.projects (id) MATCH SIMPLE
        ON UPDATE NO ACTION
        ON DELETE CASCADE
)

TABLESPACE pg_default;

ALTER TABLE public.parts
    OWNER to postgres;

-- Table: public.projects

-- DROP TABLE public.projects;

CREATE TABLE public.projects
(
    id integer NOT NULL DEFAULT nextval('projects_id_seq'::regclass),
    guild_id integer NOT NULL,
    name character varying(255) COLLATE pg_catalog."default" NOT NULL,
    description character varying(255) COLLATE pg_catalog."default",
    unique_role_id numeric,
    status project_status NOT NULL DEFAULT 'not_started'::project_status,
    self_assignment_allowed boolean NOT NULL DEFAULT false,
    max_assignments integer,
    priority_picking boolean NOT NULL DEFAULT false,
    part_restricted_upload boolean NOT NULL DEFAULT false,
    assignment_lifetime interval DEFAULT '14 days'::interval,
    manager_role_id numeric,
    main_channel_id numeric,
    info_channel_id numeric,
    cleanup_on_deletion boolean NOT NULL DEFAULT false,
    do_reminders boolean NOT NULL DEFAULT true,
    CONSTRAINT projects_pkey PRIMARY KEY (id),
    CONSTRAINT unique_name UNIQUE (guild_id, name),
    CONSTRAINT projects_guild_id_fkey FOREIGN KEY (guild_id)
        REFERENCES public.guilds (id) MATCH SIMPLE
        ON UPDATE NO ACTION
        ON DELETE NO ACTION
)

TABLESPACE pg_default;

ALTER TABLE public.projects
    OWNER to postgres;